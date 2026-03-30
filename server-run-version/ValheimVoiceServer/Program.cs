using System;
using System.Threading;
using System.Threading.Tasks;

namespace ValheimVoiceServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var config = ServerConfig.Load(args);

            Console.WriteLine("===========================================");
            Console.WriteLine(" ValheimVoiceServer - Proximity Voice Relay");
            Console.WriteLine("===========================================");
            Console.WriteLine($" UDP Port:        {config.Port}");
            Console.WriteLine($" Max Distance:    {config.MaxVoiceDistance}m");
            Console.WriteLine($" Fade Start:      {config.FadeStartDistance}m");
            Console.WriteLine($" Client Timeout:  {config.ClientTimeoutSeconds}s");
            Console.WriteLine("===========================================");
            Console.WriteLine();

            var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Shutting down...");
            };

            var relay = new VoiceRelay(config);

            try
            {
                await relay.RunAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Fatal error: {e}");
                Environment.Exit(1);
            }

            Console.WriteLine("Server stopped.");
        }
    }
}
