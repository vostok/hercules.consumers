using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Logging.Abstractions;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamWriter
    {
        private readonly StreamWriterSettings settings;
        private readonly ILog log;

        public StreamWriter(StreamWriterSettings settings, ILog log)
        {
            this.settings = settings;
            this.log = log;
        }

        public async Task WriteEvents(IList<HerculesEvent> events, CancellationToken cancellationToken)
        {
            var pointer = 0;
            while (pointer < events.Count && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var insertQuery = new InsertEventsQuery(
                        settings.TargetStreamName,
                        events.Skip(pointer).Take(settings.EventsWriteBatchSize).ToList());

                    var insertResult = await settings.GateClient
                        .InsertAsync(insertQuery, settings.EventsWriteTimeout, cancellationToken)
                        .ConfigureAwait(false);

                    insertResult.EnsureSuccess();

                    pointer += settings.EventsWriteBatchSize;
                }
                catch (Exception e)
                {
                    log.Warn(e, "Failed to send aggregated events.");
                    await Task.Delay(settings.DelayOnError, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}