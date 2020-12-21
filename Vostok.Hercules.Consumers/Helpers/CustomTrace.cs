using System;
using Vostok.Hercules.Client.Abstractions.Results;
using Vostok.Tracing.Abstractions;
using Vostok.Tracing.Extensions.Custom;

namespace Vostok.Hercules.Consumers.Helpers
{
    public class CustomTrace : IDisposable
    {
        private readonly ICustomOperationSpanBuilder spanBuilder;

        public CustomTrace(string operation, ITracer tracer, long? size = null)
        {
            spanBuilder = tracer.BeginCustomOperationSpan(operation?.Replace("Async", "") ?? "unknown");

            spanBuilder.SetTargetDetails("Hercules.Consumers", "default");
            spanBuilder.SetAnnotation(WellKnownAnnotations.Common.Component, "Hercules.Consumers");
            spanBuilder.SetOperationDetails(size);
        }

        public void SetStatus(HerculesResult status)
        {
            spanBuilder.SetOperationStatus(
                status.Status.ToString(), 
                status.IsSuccessful 
                    ? WellKnownStatuses.Success 
                    : WellKnownStatuses.Error);
        }

        public void SetSize(long? size) => spanBuilder.SetOperationDetails(size);

        public void Dispose() =>
            spanBuilder.Dispose();
    }
}