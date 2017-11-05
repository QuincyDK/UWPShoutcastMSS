﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace UWPShoutcastMSS.Streaming.Sockets
{
    public class SocketWrapper : IDisposable
    {
        protected StreamSocket BaseSocket { get; set; }
        protected DataReader SocketDataReader { get; set; }
        protected DataWriter SocketDataWriter { get; set; }
        protected SemaphoreSlim InitialBufferLock { get; set; } = new SemaphoreSlim(0, 1);
        public SocketWrapper(StreamSocket baseSocket, DataReader dataReader, DataWriter dataWriter)
        {
            if (baseSocket == null)
                throw new ArgumentNullException(nameof(baseSocket));

            if (dataReader == null)
                SocketDataReader = new DataReader(BaseSocket.InputStream);

            if (dataWriter == null)
                SocketDataWriter = new DataWriter(BaseSocket.OutputStream);

            BaseSocket = baseSocket;
            SocketDataReader = dataReader;
            SocketDataWriter = dataWriter;

            InitializeDataStream();

            InitialBufferLock.Release(1);
        }

        public Task WaitForInitialBufferReadyAsync()
        {
            return InitialBufferLock.WaitAsync();
        }

        protected SocketWrapper()
        {

        }

        protected virtual void InitializeDataStream()
        {

        }

        public virtual uint UnconsumedBufferLength { get { return SocketDataReader != null ? SocketDataReader.UnconsumedBufferLength : uint.MaxValue; } }

        public virtual async Task<uint> LoadAsync(uint amount)
        {
            await WaitForInitialBufferReadyAsync();
            var result = await SocketDataReader.LoadAsync(amount);
            InitialBufferLock.Release();
            return result;
        }

        public virtual async Task<string> ReadStringAsync(uint codeUnitCount)
        {
            await WaitForInitialBufferReadyAsync();
            var result = SocketDataReader.ReadString(codeUnitCount);
            InitialBufferLock.Release();
            return result;
        }

        public virtual async Task<IBuffer> ReadBufferAsync(uint length)
        {
            await WaitForInitialBufferReadyAsync();
            var result = SocketDataReader.ReadBuffer(length);
            InitialBufferLock.Release();
            return result;
        }

        public virtual async Task<byte> ReadByteAsync()
        {
            await WaitForInitialBufferReadyAsync();
            var result = SocketDataReader.ReadByte();
            InitialBufferLock.Release();
            return result;
        }

        public virtual async Task ReadBytesAsync(byte[] buffer)
        {
            await WaitForInitialBufferReadyAsync();
            SocketDataReader.ReadBytes(buffer);
            InitialBufferLock.Release();
        }


        protected virtual void SubclassDispose() { }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).

                    SubclassDispose();

                    SocketDataReader.Dispose();
                    SocketDataWriter.Dispose();
                    BaseSocket.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~SocketWrapper() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
