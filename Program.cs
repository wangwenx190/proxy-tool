using wangwenx190.ProxyTool;

Console.Title = "wangwenx190's Proxy Tool";
Console.Clear();
Console.CursorTop = 0;
Console.CursorVisible = false;
Configuration? configuration = Configuration.TryParse("proxy_tool_config.json");
if (configuration == null)
{
    Console.WriteLine("Failed to parse the JSON configuration file.");
    Console.ReadKey();
    return;
}
ProxyService service = new(configuration);
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    Console.WriteLine("Shutting down ...");
    service.Shutdown();
};
Thread.Sleep(-1);
