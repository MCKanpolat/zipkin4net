﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using zipkin4net.Tracers.Zipkin;
using zipkin4net.Utils;

namespace zipkin4net.Internal.Recorder
{
    internal class MutableSpanMap
    {
        private readonly IReporter<V2.Span> _reporter;
        private readonly IStatistics _statistics;
        private readonly ConcurrentDictionary<ITraceContext, MutableSpan> _spanMap = new ConcurrentDictionary<ITraceContext, MutableSpan>();
        /// <summary>
        /// Flush old records when fired.
        /// </summary>
        private readonly Timer _flushTimer;
        
        /// <summary>
        /// Spans which are not completed by this time are automatically flushed.
        /// </summary>
        internal static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(1);

        internal MutableSpanMap(IReporter<V2.Span> reporter)
            : this(reporter, new Statistics())
        {
        }
        
        internal MutableSpanMap(IReporter<V2.Span> reporter, IStatistics statistics)
        {
            _reporter = reporter;
            _statistics = statistics;
            _flushTimer = new Timer(_ => FlushOldSpans(TimeUtils.UtcNow), null, TimeToLive, TimeToLive);
        }

        /// <summary>
        /// Flush old spans which didn't complete before the end of their TTL.
        /// Visibility is set to internal to allow unit testing.
        /// </summary>
        /// <param name="utcNow"></param>
        internal void FlushOldSpans(DateTime utcNow)
        {
            var outlivedSpans = _spanMap.Where(pair => (utcNow - pair.Value.Timestamp) > TimeToLive).ToList();

            foreach (var oldSpanEntry in outlivedSpans)
            {
                if (!oldSpanEntry.Value.Finished)
                {
                    oldSpanEntry.Value.Annotate(TimeUtils.UtcNow, "flush.timeout");
                    _statistics.UpdateSpanFlushed();
                }
                RemoveThenLogSpan(oldSpanEntry.Key);
            }
        }
        
        private void RemoveThenLogSpan(ITraceContext spanState)
        {
            MutableSpan spanToLog;
            if (_spanMap.TryRemove(spanState, out spanToLog))
            {
                _reporter.Report(spanToLog.ToSpan());
            }
        }

        public MutableSpan GetOrCreate(ITraceContext traceContext, Func<ITraceContext, MutableSpan> spanCreator)
        {
            return _spanMap.GetOrAdd(traceContext, spanCreator);
        }

        public MutableSpan Remove(ITraceContext traceContext)
        {
            MutableSpan span = null;
            _spanMap.TryRemove(traceContext, out span);
            return span; //Will return null if span doesn't exist
        }
    }
}