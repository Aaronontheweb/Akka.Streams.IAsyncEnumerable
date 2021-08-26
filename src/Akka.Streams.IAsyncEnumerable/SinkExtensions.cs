﻿// //-----------------------------------------------------------------------
// // <copyright file="AsyncEnumerable.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Configuration.Hocon;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Used to treat an <see cref="IRunnableGraph{TMat}"/> of <see cref="ISinkQueue{T}"/>
    /// as an <see cref="IAsyncEnumerable{T}"/>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class StreamsAsyncEnumerableRerunnable<T,TMat> : IAsyncEnumerable<T>
    {
        private static readonly Sink<T, ISinkQueue<T>> defaultSinkqueue =
            Sink.Queue<T>();
        private readonly Source<T, TMat> _source;
        private readonly IMaterializer _materializer;

        private readonly Sink<T, ISinkQueue<T>> thisSinkQueue;
        //private readonly IRunnableGraph<(UniqueKillSwitch, ISinkQueue<T>)> _graph;
        public StreamsAsyncEnumerableRerunnable(Source<T,TMat> source, IMaterializer materializer)
        {
            _source = source;
            _materializer = materializer;
            thisSinkQueue = defaultSinkqueue;
        }

        public StreamsAsyncEnumerableRerunnable(Source<T, TMat> source,
            IMaterializer materializer, int minBuf, int maxBuf):this(source, materializer)
        {
            thisSinkQueue =
                defaultSinkqueue.WithAttributes(
                    Attributes.CreateInputBuffer(minBuf, maxBuf));
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return new SinkQueueAsyncEnumerator<T>(_source
                    .Via(cancellationToken.AsFlow<T>(cancelGracefully: true))
                    .ViaMaterialized(KillSwitches.Single<T>(), Keep.Right)
                    .ToMaterialized(thisSinkQueue, Keep.Both)
                    .Run(_materializer),
                cancellationToken);
        }
    }
    /// <summary>
    /// Wraps a Sink Queue and Killswitch around <see cref="IAsyncEnumerator{T}"/>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class SinkQueueAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private ISinkQueue<T> _sinkQueue;
        private IKillSwitch _killSwitch;
        private CancellationToken _token;
        public SinkQueueAsyncEnumerator((UniqueKillSwitch killSwitch,ISinkQueue<T> sinkQueue) queueAndSwitch, CancellationToken token)
        {
            _sinkQueue = queueAndSwitch.sinkQueue;
            _killSwitch = queueAndSwitch.killSwitch;
            _token = token;
        }
        public async ValueTask DisposeAsync()
        {
            //If we are disposing, let's shut down the stream
            //so that we don't have data hanging around.
            _killSwitch.Shutdown();
            _killSwitch = null;
            _sinkQueue = null;
        }

        public async ValueTask<bool> MoveNextAsync()
        {
            _token.ThrowIfCancellationRequested();
            var opt = await _sinkQueue.PullAsync();
            if (opt.HasValue)
            {
                Current = opt.Value;
                return true;
            }
            else
            {
                return false;
            }
        }

        public T Current { get; private set; }
    }
    
    public static class SourceExtenions{
        /// <summary>
        /// Shortcut for running this <see cref="Source{TOut,TMat}"/> as an <see cref="IAsyncEnumerable{TOut}"/>.
        /// The given enumerable is re-runnable but will cause a re-materialization of the stream each time.
        /// This is implemented using a SourceQueue and will buffer elements based on configured stream defaults.
        /// For custom buffers Please use <see cref="RunAsAsyncEnumerableBuffer"/>
        /// </summary>
        /// <param name="source">The source to consume.</param>
        /// <param name="materializer">The materializer to use for each enumeration</param>
        /// <returns>A lazy <see cref="IAsyncEnumerable{T}"/> that will run each time it is enumerated.</returns>
        public static IAsyncEnumerable<TOut> RunAsAsyncEnumerable<TOut,TMat>(this Source<TOut, TMat> source,
            IMaterializer materializer) =>
            new StreamsAsyncEnumerableRerunnable<TOut,TMat>(source, materializer);

        /// <summary>
        /// Shortcut for running this <see cref="Source{TOut,TMat}"/> as an <see cref="IAsyncEnumerable{TOut}"/>.
        /// The given enumerable is re-runnable but will cause a re-materialization of the stream each time.
        /// This is implemented using a SourceQueue and will buffer elements and/or backpressure,
        /// based on the buffer values provided.
        /// </summary>
        /// <param name="source">Source</param>
        /// <param name="materializer">The materializer to use for each enumeration</param>
        /// <param name="minBuffer">The minimum input buffer size</param>
        /// <param name="maxBuffer">The Max input buffer size.</param>
        /// <returns>A lazy <see cref="IAsyncEnumerable{T}"/> that will run each time it is enumerated.</returns>
        public static IAsyncEnumerable<TOut> RunAsAsyncEnumerableBuffer<TOut,TMat>(this Source<TOut, TMat> source,
            IMaterializer materializer, int minBuffer = 4,
            int maxBuffer = 16) =>
            new StreamsAsyncEnumerableRerunnable<TOut,TMat>(source, materializer,minBuffer,maxBuffer);

    }
}