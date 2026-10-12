// Compiled next to the actual LuaLit class extracted from Core.cs; no parser reimplementation.
public static class LuaLitTests
{
    static int checks;
    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
    public static void Run()
    {
        var t = LuaLit.Parse("{[\"started\"]=true,[\"speed\"]=2,[1]={[\"n\"]=4,[3]=false},[2]=nil}") as Dictionary<object, object>;
        Check(t != null && (bool)t["started"] && (double)t["speed"] == 2d, "session values");
        var args = t[1L] as Dictionary<object, object>;
        Check(args != null && (double)args["n"] == 4 && !(bool)args[3L] && !t.ContainsKey(2L), "sparse numeric keys/nil");
        var escapes = "";
        for (int i = 0; i < 256; i++) escapes += "\\" + i.ToString("D3");
        var text = LuaLit.Parse("\"" + escapes + "\"") as string;
        Check(text != null && text.Length == 256 && text.Select((c,i) => c == i).All(x => x), "decimal byte escapes");
        var unicode = "Привет\n\t\r\"\\";
        Check((string)LuaLit.Parse(LuaLit.Quote(unicode)) == unicode, "launcher quote roundtrip");
        Check((double)LuaLit.Parse("-1E+30") == -1e30 && (double)LuaLit.Parse(".5") == .5d, "decimal/exponents");
        Check(LuaLit.Parse("{[false]=true}") is Dictionary<object, object>, "boolean key");
        Check((string)LuaLit.Parse("'quoted'") == "quoted", "single-quoted string");
        Check(LuaLit.Parse("\r\n{[\"a\"]=1,}\t") is Dictionary<object, object>, "whitespace/trailing comma");
        foreach (var bad in new[] { "{garbage}", "{[\"x\"]=1", "{[\"x\"]1}", "{[\"x\"]=}",
            "{[nil]=1}", "{[{}]=1}", "{[1]=nil,[1.0]=2}", "{[\"x\"]=1,[\"x\"]=2}",
            "{} garbage", "truex", "1e", "+1", "--1", "0x10", "1.2.3", "1e309", "-1e309",
            "\"bad\\q\"", "\"bad\\999\"", "\"unfinished", "\"raw\nline\"", "{1,2}",
            "(function() while true do end end)()", "{[\"x\"]=(function() return 7 end)()}", "1+2", "_G" })
            Check(LuaLit.Parse(bad) == null, "reject " + bad.Replace("\n", "<LF>"));
        var deep = "{}";
        for (int i=1; i < LuaLit.MaxDepth; i++) deep = "{[1]=" + deep + "}";
        Check(LuaLit.Parse(deep) != null, "depth boundary");
        Check(LuaLit.Parse("{[1]=" + deep + "}") == null, "depth overflow");
        var fields = new StringBuilder("{");
        for (int i=0; i < LuaLit.MaxEntries; i++) { if(i>0) fields.Append(','); fields.Append('[').Append(i).Append("]=nil"); }
        var valid = fields.ToString() + "}";
        Check(LuaLit.Parse(valid) != null, "entry boundary");
        Check(LuaLit.Parse(fields.ToString() + ",[100001]=nil}") == null, "entry overflow");
        Check(LuaLit.Parse("{[\"child\"]=" + valid + "}") == null, "aggregate entries");
        Check(((string)LuaLit.Parse("\"" + new string('a', LuaLit.MaxString) + "\"")).Length == LuaLit.MaxString, "string boundary");
        Check(LuaLit.Parse("\"" + new string('a', LuaLit.MaxString+1) + "\"") == null, "string overflow");
        Check(LuaLit.Parse("{}" + new string(' ',LuaLit.MaxBytes-2)) != null, "message boundary");
        Check(LuaLit.Parse("{}" + new string(' ',LuaLit.MaxBytes-1)) == null, "message overflow");
        Check(LuaLit.Parse(null) == null && LuaLit.Parse("") == null, "null/empty input");
        var chunk = Convert.ToBase64String(new byte[256*1024]);
        Check(((Dictionary<object, object>)LuaLit.Parse("{[\"data\"]=\"" + chunk + "\"}"))["data"].Equals(chunk), "save chunk");
        var bigKey = LuaLit.Parse("{[1e30]=1}") as Dictionary<object,object>;
        Check(bigKey != null && bigKey.ContainsKey(1e30), "large finite key not cast to long");
        foreach(var bad in new[]{"\"a\0b\"","\"a\tb\"","\"a\u007fb\""}) Check(LuaLit.Parse(bad)==null,"fast string rejects raw control");
        var controls=new string(Enumerable.Range(0,128).Select(i=>(char)i).ToArray());
        Check((string)LuaLit.Parse(LuaLit.Quote(controls))==controls,"quote roundtrip includes every ASCII control and DEL");
        Console.WriteLine("C# decoder: " + checks + " checks passed (no game)");
    }
}
