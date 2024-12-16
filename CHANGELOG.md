## 2.0.4 (16-12-2024): 

Bump NuGet deps versions

## 2.0.3 (11-12-2023):

Add null check in `StreamBinaryEventsWriter.FlushAsync` to prevent NRE if there were no calls to `Put`

## 2.0.2 (29-11-2022):

StreamConsumer: added supports for recall RunAsync method after cancellation

## 2.0.1 (01-09-2022):

Use `IBinaryEventsReader` instead of `IBinaryBufferReader` in event consumers.

## 1.0.1 (17-08-2022):

Made `StreamBinaryEventsWriter` asynchronous and parallel.

## 0.1.10 (15-08-2022):

Save final `WindowedStreamConsumer` coordinates.

## 0.1.9 (03-08-2022):

Added `OnEventDrop` callback for `WindowedStreamConsumer`.

## 0.1.8 (03-02-2022):

Changed `MetricContext` in `BatchesStreamConsumer` to `InstanceMetricContext`. Added `ApplicationMetricContext` and a new metric to `WindowedStreamConsumer`.

## 0.1.7 (06-12-2021):

Added `net6.0` target.

## 0.1.5 (25-11-2021):

Logging improvements.

## 0.1.4 (25-01-2021):

Fixed custom spans.

## 0.1.3 (20-01-2021):

Trace operations with custom spans.

More consumers templates.

## 0.1.2 (08-02-2020):

Fix restart coordinates.

## 0.1.1 (26-10-2019):

Added more readers.

## 0.1.0 (15-07-2019): 

Initial prerelease.