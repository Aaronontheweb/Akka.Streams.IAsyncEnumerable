using System;
using NBench;

namespace Akka.Streams.IAsyncEnumerable.Tests.Performance
{
    class Program
    {
        static int Main(string[] args)
        {
            return NBenchRunner.Run<Program>();
        }
    }
}
