using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

// JetPoa.Backup — cópia lógica do esquema BISTRO (estrutura + dados) num .zip, e restauração em outro Oracle.
// Não depende do exp/expdp nem da versão do cliente Oracle instalado.
//
//   JetPoa.Backup exportar  <arquivo.zip> [--conf <arquivo.conf>]
//   JetPoa.Backup verificar <arquivo.zip>
//   JetPoa.Backup restaurar <arquivo.zip> [--conf <arquivo.conf> | --conexao "<connection string>"]
//
// A conexão vem de ConnectionStrings__Bistro no .conf (padrão C:\Projeto\certificado\52822987000123.conf).
// Restaurar exige o esquema de destino vazio (usuário criado pelo db\00 e db\00b, sem tabelas).

return await Backup.Executar(args);

static class Backup
{
    const string ConfPadrao = @"C:\Projeto\certificado\52822987000123.conf";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<int> Executar(string[] args)
    {
        try
        {
            if (args.Length < 2) return Uso();
            var arquivo = Path.GetFullPath(args[1]);
            string? Opcao(string nome) { var i = Array.IndexOf(args, nome); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            string Conexao() => Opcao("--conexao") ?? LerConf(Opcao("--conf") ?? ConfPadrao);
            switch (args[0].ToLowerInvariant())
            {
                case "exportar": await Exportar(Conexao(), arquivo); break;
                case "verificar": Verificar(arquivo); break;
                case "restaurar": await Restaurar(Conexao(), arquivo); break;
                default: return Uso();
            }
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("ERRO: " + e.Message);
            return 1;
        }
    }

    static int Uso()
    {
        Console.Error.WriteLine("Uso: JetPoa.Backup exportar|verificar|restaurar <arquivo.zip> [--conf <arquivo.conf>] [--conexao \"...\"]");
        return 2;
    }

    static string LerConf(string caminho)
    {
        var linha = File.ReadAllLines(caminho).Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("ConnectionStrings__Bistro=", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"ConnectionStrings__Bistro não encontrada em {caminho}.");
        return linha[(linha.IndexOf('=') + 1)..];
    }

    sealed record Ddl(string Etapa, string Nome, string Sql);
    sealed record Coluna(string Nome, string Tipo);
    sealed record Tabela(string Nome, List<Coluna> Colunas, long Linhas);
    sealed record Manifesto(int Versao, DateTime GeradoEm, string Esquema, string BancoVersao, List<Tabela> Tabelas, int ObjetosDdl);

    // ordem de restauração: sequências, tabelas (sem chaves estrangeiras), dados, índices, chaves estrangeiras, triggers
    static readonly string[] Etapas = ["SEQUENCIA", "TABELA", "INDICE", "FK", "TRIGGER"];

    // ------------------------------------------------------------------ exportar
    static async Task Exportar(string conexao, string arquivo)
    {
        using var c = new OracleConnection(conexao);
        await c.OpenAsync();
        var esquema = Escalar<string>(c, null, "select user from dual");
        var versao = c.ServerVersion;

        // retrato consistente: todas as tabelas no mesmo instante
        using var tx = c.BeginTransaction();
        Executar(c, tx, "set transaction read only");

        var ddl = new List<Ddl>();
        Executar(c, tx, """
            begin
              dbms_metadata.set_transform_param(dbms_metadata.session_transform, 'STORAGE', false);
              dbms_metadata.set_transform_param(dbms_metadata.session_transform, 'SEGMENT_ATTRIBUTES', true);
              dbms_metadata.set_transform_param(dbms_metadata.session_transform, 'REF_CONSTRAINTS', false);
              dbms_metadata.set_transform_param(dbms_metadata.session_transform, 'SQLTERMINATOR', false);
            end;
            """);
        foreach (var s in Lista(c, tx, "select sequence_name from user_sequences order by 1"))
            ddl.Add(new("SEQUENCIA", s, Metadado(c, tx, "SEQUENCE", s)));
        var tabelas = Lista(c, tx, "select table_name from user_tables where temporary = 'N' and table_name not like 'BIN$%' order by 1");
        foreach (var t in tabelas)
            ddl.Add(new("TABELA", t, Metadado(c, tx, "TABLE", t)));
        // índices que não são de PK/UNIQUE (esses vêm com a tabela) nem de LOB
        foreach (var i in Lista(c, tx, """
            select i.index_name from user_indexes i
             where i.index_type <> 'LOB' and i.table_name in (select table_name from user_tables where temporary = 'N')
               and not exists (select 1 from user_constraints k where k.index_name = i.index_name and k.constraint_type in ('P','U'))
             order by 1
            """))
            ddl.Add(new("INDICE", i, Metadado(c, tx, "INDEX", i)));
        foreach (var k in Lista(c, tx, "select constraint_name from user_constraints where constraint_type = 'R' order by table_name, constraint_name"))
            ddl.Add(new("FK", k, Metadado(c, tx, "REF_CONSTRAINT", k)));
        foreach (var g in Lista(c, tx, "select trigger_name from user_triggers order by 1"))
            ddl.Add(new("TRIGGER", g, Metadado(c, tx, "TRIGGER", g)));

        var temp = arquivo + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        var lista = new List<Tabela>();
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var t in tabelas)
            {
                var entrada = zip.CreateEntry($"dados/{t}.jsonl", CompressionLevel.Optimal);
                using var w = new StreamWriter(entrada.Open(), new UTF8Encoding(false));
                using var cmd = Comando(c, tx, $"select * from \"{t}\"");
                cmd.InitialLOBFetchSize = -1;
                using var r = cmd.ExecuteReader();
                var cols = Enumerable.Range(0, r.FieldCount).Select(i => new Coluna(r.GetName(i), r.GetDataTypeName(i).ToUpperInvariant())).ToList();
                long n = 0;
                while (r.Read())
                {
                    var linha = new JsonArray();
                    for (var i = 0; i < cols.Count; i++) linha.Add(Valor(r, i, cols[i].Tipo));
                    w.WriteLine(linha.ToJsonString());
                    n++;
                }
                lista.Add(new Tabela(t, cols, n));
            }
            await Escrever(zip, "ddl.json", JsonSerializer.Serialize(ddl, Json));
            await Escrever(zip, "ddl.sql", string.Join("\n\n", ddl.Select(d => $"-- {d.Etapa} {d.Nome}\n{d.Sql.Trim()}\n/")));
            await Escrever(zip, "manifesto.json", JsonSerializer.Serialize(new Manifesto(1, DateTime.Now, esquema, versao, lista, ddl.Count), Json));
        }
        tx.Rollback();
        File.Move(temp, arquivo, overwrite: true);
        Console.WriteLine($"Exportado {esquema}: {lista.Count} tabelas, {lista.Sum(t => t.Linhas)} linhas, {ddl.Count} objetos -> {arquivo} ({new FileInfo(arquivo).Length / 1024} KB)");
    }

    static JsonNode? Valor(OracleDataReader r, int i, string tipo)
    {
        if (r.IsDBNull(i)) return null;
        return tipo switch
        {
            "DECIMAL" or "NUMBER" or "INT16" or "INT32" or "INT64" or "DOUBLE" or "SINGLE" =>
                JsonValue.Create(r.GetOracleDecimal(i).ToString()),   // texto: preserva todas as casas
            "DATE" => JsonValue.Create(r.GetDateTime(i).ToString("yyyy-MM-ddTHH:mm:ss", Inv)),
            "TIMESTAMP" or "TIMESTAMPTZ" or "TIMESTAMPLTZ" => JsonValue.Create(r.GetOracleTimeStamp(i).Value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", Inv)),
            "BLOB" or "RAW" or "LONGRAW" => JsonValue.Create(Convert.ToBase64String((byte[])r.GetValue(i))),
            _ => JsonValue.Create(r.GetValue(i) is string s ? s : Convert.ToString(r.GetValue(i), Inv)),
        };
    }

    static string Metadado(OracleConnection c, OracleTransaction tx, string tipo, string nome)
    {
        using var cmd = Comando(c, tx, "select dbms_metadata.get_ddl(:t, :n) from dual");
        cmd.Parameters.Add("t", tipo);
        cmd.Parameters.Add("n", nome);
        cmd.InitialLOBFetchSize = -1;
        return ((string)cmd.ExecuteScalar()!).Trim();
    }

    // ------------------------------------------------------------------ verificar
    static void Verificar(string arquivo)
    {
        using var zip = ZipFile.OpenRead(arquivo);
        var m = LerManifesto(zip);
        var ddl = LerDdl(zip);
        var erros = new List<string>();
        if (ddl.Count != m.ObjetosDdl) erros.Add($"ddl.json tem {ddl.Count} objetos, manifesto diz {m.ObjetosDdl}");
        foreach (var t in m.Tabelas)
        {
            var e = zip.GetEntry($"dados/{t.Nome}.jsonl");
            if (e is null) { erros.Add($"faltam os dados de {t.Nome}"); continue; }
            using var rd = new StreamReader(e.Open());
            long n = 0;
            while (rd.ReadLine() is { } l) { if (JsonNode.Parse(l) is not JsonArray a || a.Count != t.Colunas.Count) erros.Add($"{t.Nome}: linha {n + 1} inválida"); n++; }
            if (n != t.Linhas) erros.Add($"{t.Nome}: {n} linhas, manifesto diz {t.Linhas}");
            if (!ddl.Any(d => d.Etapa == "TABELA" && d.Nome == t.Nome)) erros.Add($"{t.Nome}: sem estrutura");
        }
        if (erros.Count > 0) throw new InvalidOperationException("Backup inválido: " + string.Join("; ", erros.Take(10)));
        Console.WriteLine($"OK: {m.Esquema} de {m.GeradoEm:dd/MM/yyyy HH:mm}, {m.Tabelas.Count} tabelas, {m.Tabelas.Sum(t => t.Linhas)} linhas, {ddl.Count} objetos.");
    }

    static Manifesto LerManifesto(ZipArchive zip) =>
        JsonSerializer.Deserialize<Manifesto>(Ler(zip, "manifesto.json")) ?? throw new InvalidOperationException("manifesto.json ilegível");

    static List<Ddl> LerDdl(ZipArchive zip) =>
        JsonSerializer.Deserialize<List<Ddl>>(Ler(zip, "ddl.json")) ?? throw new InvalidOperationException("ddl.json ilegível");

    static string Ler(ZipArchive zip, string nome)
    {
        using var rd = new StreamReader((zip.GetEntry(nome) ?? throw new InvalidOperationException($"{nome} não está no backup")).Open());
        return rd.ReadToEnd();
    }

    static async Task Escrever(ZipArchive zip, string nome, string texto)
    {
        using var w = new StreamWriter(zip.CreateEntry(nome, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        await w.WriteAsync(texto);
    }

    // ------------------------------------------------------------------ restaurar
    static async Task Restaurar(string conexao, string arquivo)
    {
        Verificar(arquivo);
        using var zip = ZipFile.OpenRead(arquivo);
        var m = LerManifesto(zip);
        var ddl = LerDdl(zip);

        using var c = new OracleConnection(conexao);
        await c.OpenAsync();
        var destino = Escalar<string>(c, null, "select user from dual");
        var existentes = Escalar<decimal>(c, null, "select count(*) from user_tables");
        if (existentes > 0)
            throw new InvalidOperationException($"O esquema {destino} já tem {existentes} tabelas. Restaure num esquema vazio (veja docs\\04-backup.md).");
        // o DDL traz o nome do esquema de origem: troca pelo de destino
        string Ajustar(string sql) => sql.Replace($"\"{m.Esquema}\".", $"\"{destino}\".");

        void Etapa(string etapa)
        {
            foreach (var d in ddl.Where(d => d.Etapa == etapa))
                foreach (var sql in Separar(d))
                    try { Executar(c, null, Ajustar(sql)); }
                    catch (OracleException e) { throw new InvalidOperationException($"{etapa} {d.Nome}: {e.Message}\n{sql}"); }
            Console.WriteLine($"  {etapa}: {ddl.Count(d => d.Etapa == etapa)}");
        }

        Etapa("SEQUENCIA");
        Etapa("TABELA");

        // dados antes dos triggers e chaves estrangeiras: ids originais preservados, sem ordem entre tabelas
        foreach (var t in m.Tabelas)
        {
            if (t.Linhas == 0) continue;
            var cols = t.Colunas;
            var sql = $"insert into \"{t.Nome}\" ({string.Join(", ", cols.Select(x => $"\"{x.Nome}\""))}) values ({string.Join(", ", cols.Select((_, i) => $":p{i}"))})";
            using var tx = c.BeginTransaction();
            using var rd = new StreamReader(zip.GetEntry($"dados/{t.Nome}.jsonl")!.Open());
            long n = 0;
            while (rd.ReadLine() is { } l)
            {
                var a = (JsonArray)JsonNode.Parse(l)!;
                using var cmd = Comando(c, tx, sql);
                for (var i = 0; i < cols.Count; i++) cmd.Parameters.Add(Parametro($"p{i}", cols[i].Tipo, a[i]));
                cmd.ExecuteNonQuery();
                n++;
            }
            tx.Commit();
            var conferido = Escalar<decimal>(c, null, $"select count(*) from \"{t.Nome}\"");
            if (conferido != t.Linhas) throw new InvalidOperationException($"{t.Nome}: gravou {conferido} de {t.Linhas} linhas.");
        }
        Console.WriteLine($"  DADOS: {m.Tabelas.Count} tabelas, {m.Tabelas.Sum(t => t.Linhas)} linhas");

        Etapa("INDICE");
        Etapa("FK");
        Etapa("TRIGGER");
        var invalidos = Lista(c, null, "select object_type || ' ' || object_name from user_objects where status <> 'VALID'");
        if (invalidos.Count > 0) throw new InvalidOperationException("Objetos inválidos após restaurar: " + string.Join(", ", invalidos));
        Console.WriteLine($"Restaurado em {destino} o backup de {m.Esquema} de {m.GeradoEm:dd/MM/yyyy HH:mm}.");
    }

    /// <summary>get_ddl de trigger vem com o ALTER TRIGGER ... ENABLE junto: separa em dois comandos.</summary>
    static IEnumerable<string> Separar(Ddl d)
    {
        var sql = d.Sql.Trim();
        if (d.Etapa != "TRIGGER") { yield return sql.TrimEnd(';', '/', ' ', '\n', '\r'); yield break; }
        var i = sql.IndexOf("ALTER TRIGGER", StringComparison.OrdinalIgnoreCase);
        if (i < 0) { yield return sql; yield break; }
        yield return sql[..i].Trim().TrimEnd('/').Trim();
        yield return sql[i..].Trim().TrimEnd(';', '/').Trim();
    }

    static OracleParameter Parametro(string nome, string tipo, JsonNode? v)
    {
        var p = new OracleParameter { ParameterName = nome };
        string? s = v?.GetValue<string>();
        switch (tipo)
        {
            case "DECIMAL" or "NUMBER" or "INT16" or "INT32" or "INT64" or "DOUBLE" or "SINGLE":
                p.OracleDbType = OracleDbType.Decimal;
                p.Value = s is null ? DBNull.Value : OracleDecimal.Parse(s);
                break;
            case "DATE":
                p.OracleDbType = OracleDbType.Date;
                p.Value = s is null ? DBNull.Value : DateTime.ParseExact(s, "yyyy-MM-ddTHH:mm:ss", Inv);
                break;
            case "TIMESTAMP" or "TIMESTAMPTZ" or "TIMESTAMPLTZ":
                p.OracleDbType = OracleDbType.TimeStamp;
                p.Value = s is null ? DBNull.Value : DateTime.ParseExact(s, "yyyy-MM-ddTHH:mm:ss.fffffff", Inv);
                break;
            case "BLOB" or "RAW" or "LONGRAW":
                p.OracleDbType = tipo == "BLOB" ? OracleDbType.Blob : OracleDbType.Raw;
                p.Value = s is null ? DBNull.Value : Convert.FromBase64String(s);
                break;
            case "CLOB" or "NCLOB":
                p.OracleDbType = OracleDbType.Clob;
                p.Value = (object?)s ?? DBNull.Value;
                break;
            default:
                p.OracleDbType = OracleDbType.Varchar2;
                p.Value = (object?)s ?? DBNull.Value;
                break;
        }
        return p;
    }

    // ------------------------------------------------------------------ apoio
    static OracleCommand Comando(OracleConnection c, OracleTransaction? tx, string sql)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.BindByName = true;
        if (tx is not null) cmd.Transaction = tx;
        return cmd;
    }

    static void Executar(OracleConnection c, OracleTransaction? tx, string sql)
    {
        using var cmd = Comando(c, tx, sql);
        cmd.ExecuteNonQuery();
    }

    static T Escalar<T>(OracleConnection c, OracleTransaction? tx, string sql)
    {
        using var cmd = Comando(c, tx, sql);
        return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T), Inv);
    }

    static List<string> Lista(OracleConnection c, OracleTransaction? tx, string sql)
    {
        using var cmd = Comando(c, tx, sql);
        using var r = cmd.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetString(0));
        return l;
    }
}
