using System;
using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;
using Teamnet.DirectoryWatcher;

namespace ConsoleApp
{
    class Server
    {
        static void Main(string[] args)
        {
            MediaFoundationApi.Startup();
            using(var directoryWatcher = new DirectoryWatcher { Path = @"D:\", SearchPattern = "*.wav" })
            {
                directoryWatcher.Error += OnError;
                directoryWatcher.HandleFile += OnHandleFile;
                directoryWatcher.Start();

                Console.WriteLine("Server started...");
                Console.ReadLine();
            }
            MediaFoundationApi.Shutdown();
        }

        private static void OnHandleFile(object sender, FileEventArgs e)
        {
            JobsRunner.QueueUserWorkItem(_=> WavToMp3(e.Path));
        }

        private static void WavToMp3(string path)
        {
            using(var reader = new WaveFileReader(path))
            {
                var outputFileName = Path.ChangeExtension(path, ".mp3");
                MediaFoundationEncoder.EncodeToMp3(reader, outputFileName);
            }
            // we may need a policy around deleting files when we fail
            // a temporary failure should allow retry
            // a permanent one should not
            File.Delete(path);
        }

        private static void OnError(object sender, ErrorEventArgs e)
        {
            Console.WriteLine(e.GetException());
        }
    }
}