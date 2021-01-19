using Vostok.Tracing.Abstractions;
using Vostok.Tracing.Extensions.Custom;

namespace Vostok.Hercules.Consumers.Helpers
{
    internal static class TracerExtensions
    {
        public static ICustomOperationSpanBuilder BeginConsumerCustomOperationSpan(this ITracer tracer, string operationName)
        {
            var spanBuilder = tracer.BeginCustomOperationSpan(operationName);
            spanBuilder.SetOperationStatus(null, WellKnownStatuses.Success);
            spanBuilder.SetTargetDetails("Hercules.Consumers", "default");
            spanBuilder.SetAnnotation(WellKnownAnnotations.Common.Component, "Hercules.Consumers");
            return spanBuilder;
        }
    }
}