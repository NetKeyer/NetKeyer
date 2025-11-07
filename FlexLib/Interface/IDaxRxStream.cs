namespace Flex.Smoothlake.FlexLib.Interface
{
    public interface IDaxRxStream : IDaxStream
    {
        int BytesPerSecFromRadio { get; }
        int ErrorCount { get; }
        int TotalCount { get; }

        event RXAudioStream.DataReadyEventHandler DataReady;
    }
}