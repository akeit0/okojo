using System.Text;
using Okojo.Node.Cli;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

return await NodeCliApplication.RunAsync(args);
