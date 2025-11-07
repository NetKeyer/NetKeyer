namespace Flex.Smoothlake.FlexLib.Interface
{
    public interface IDaxTxStream: IDaxStream
    {
        void AddTXData(float[] tx_data_stereo, bool sendReducedBW);
        bool Transmit { get; set; }
    }
}