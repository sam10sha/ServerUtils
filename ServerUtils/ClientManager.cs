using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;

#if DEBUG
using System.Diagnostics;
#endif

namespace ServerUtils
{
    /// <summary>
    /// A convenience class to manage client connections. This class
    /// will handle all communication with the client, and allow
    /// a custom reply to be sent to the client in response to a message
    /// that the client has provided.
    /// </summary>
    public class ClientManager
    {
        /// <summary>
        /// The number of bytes to expect when transmitting the size
        /// value of a message
        /// </summary>
        private static int NUM_BYTES_MSG_SIZE_DESCRIPTOR = 4;

        /// <summary>
        /// A class containing the current stage of operation of the
        /// ClientManager.
        /// </summary>
        private class StateObject
        {
            /// <summary>
            /// An enumerator describing the current stage of the
            /// management cycle
            /// </summary>
            public enum CURRENTCONNECTIONSTAGE
            {
                RECEIVE_REQUEST_SIZE,
                WAITING_RECEIVE_SIZE,
                RECEIVE_REQUEST_MSG,
                RETRYING_RECEIVE_REQUEST_MSG,
                WAITING_RECEIVE_MSG,
                LOADING_RESPONSE_IN_BUFFER,
                SENDING_RESPONSE_SIZE,
                WAITING_SEND_SIZE,
                SENDING_RESPONSE_MSG,
                WAITING_SEND_MSG,
                ERROR,
                COMPLETE
            }

            /// <summary>
            /// The current stage of the management process.\n
            /// This is a shared variable. Usage of this
            /// variable must be carefully monitored
            /// </summary>
            internal CURRENTCONNECTIONSTAGE CurrentStage;

            /// <summary>
            /// A buffer to temporarily hold the size of the buffer.
            /// </summary>
            internal int TransmissionSize;

            /// <summary>
            /// A buffer to temporarily hold the number of bytes most
            /// recently transmitted.
            /// 
            /// Mainly intended for keeping track of the number of bytes
            /// transmitted while performing partial transactions.
            /// </summary>
            internal int TransmittedBytes;

            /// <summary>
            /// A buffer to temporarily hold data.
            /// </summary>
            internal byte[] Buffer;
        }


        /// <summary>
        /// Callback function for the action to perform when loading
        /// a response to return to the client
        /// </summary>
        /// 
        /// <param name="RecvRequest">
        /// The bytes received from the client's original request
        /// </param>
        /// 
        /// <returns>
        /// Bytes to send to the client
        /// </returns>
        public delegate byte[] LoadResponseBufferDelegate(byte[] RecvRequest, ref bool TerminationFlag);


        /// <summary>
        /// The socket representing a client to service
        /// </summary>
        private Socket myClientSocket;

        /// <summary>
        /// A callback function which will be invoked when the ClientManager
        /// requires a response to send back to the client.\n
        /// 
        /// If no callback function is provided, or if the function returns no
        /// bytes to send back to the client, the ClientManager will respond with
        /// a standard response
        /// </summary>
        private LoadResponseBufferDelegate ResponseDelegate;

        /// <summary>
        /// A class containing the current state of operations of the
        /// ClientManager
        /// </summary>
        private StateObject StateObj;

        /// <summary>
        /// Initilizer for the ClientManager class.
        /// </summary>
        /// 
        /// <param name="ClientSocket">
        /// The socket connection to a client for the ClientManager
        /// to handle.
        /// </param>
        public ClientManager(Socket ClientSocket)
        {
            myClientSocket = ClientSocket;
            ResponseDelegate = null;

            StateObj = new StateObject();
            StateObj.TransmissionSize = 0;
            StateObj.Buffer = null;
            StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.RECEIVE_REQUEST_SIZE;
        }

        // Public properties

        /// <summary>
        /// Sets the callback function in order to provide a custom
        /// response to the client.
        /// </summary>
        public LoadResponseBufferDelegate ResponseCallback
        {
            set
            {
                ResponseDelegate = value;
            }
        }

        /// <summary>
        /// A property describing whether or not the ClientManager's operations
        /// have halted. The ClientManager can halt in the event of success or
        /// failure.
        /// </summary>
        public bool Finished
        {
            get
            {
                bool Error = StateObj.CurrentStage == StateObject.CURRENTCONNECTIONSTAGE.ERROR;
                bool Completion = StateObj.CurrentStage == StateObject.CURRENTCONNECTIONSTAGE.COMPLETE;
                return Error || Completion;
            }
        }


        // Public member functiosn

        /// <summary>
        /// Procedure for servicing a client
        /// </summary>
        /// 
        /// <param name="TerminationFlag">
        /// A reference to a termination flag that the ClientManager
        /// should monitor
        /// </param>
        /// 
        /// <returns>
        /// True if the operation was successful in completing the task.
        /// False if the operation failed
        /// </returns>
        public bool ServiceClientCycle(ref bool TerminationFlag)
        {
            while (!TerminationFlag && !Finished)
            {
                /* The commented out code in this switch statement imply that
                 * the default branch should be taken and that the ClientManager
                 * should do nothing while in these states
                 */
                switch (StateObj.CurrentStage)
                {
                    case StateObject.CURRENTCONNECTIONSTAGE.RECEIVE_REQUEST_SIZE:
                        ReceiveClientMsgSize();
                        break;
                    /* case StateObject.CURRENTCONNECTIONSTAGE.WAITING_RECEIVE_SIZE:
                        break; */
                    case StateObject.CURRENTCONNECTIONSTAGE.RECEIVE_REQUEST_MSG:
                        ReceiveClientMsg();
                        break;
                    case StateObject.CURRENTCONNECTIONSTAGE.RETRYING_RECEIVE_REQUEST_MSG:
                        RetryReceiveMsg();
                        break;
                    /* case StateObject.CURRENTCONNECTIONSTAGE.WAITING_RECEIVE_MSG:
                        break; */
                    case StateObject.CURRENTCONNECTIONSTAGE.LOADING_RESPONSE_IN_BUFFER:
                        LoadServerResponseInBuffer(ref TerminationFlag);
                        break;
                    case StateObject.CURRENTCONNECTIONSTAGE.SENDING_RESPONSE_SIZE:
                        SendResponseMsgSize();
                        break;
                    /* case StateObject.CURRENTCONNECTIONSTAGE.WAITING_SEND_SIZE:
                        break; */
                    case StateObject.CURRENTCONNECTIONSTAGE.SENDING_RESPONSE_MSG:
                        SendResponseMsg();
                        break;
                    /* case StateObject.CURRENTCONNECTIONSTAGE.WAITING_SEND_MSG:
                        break; */
                    default:
                        break;
                }
            }
            return StateObj.CurrentStage == StateObject.CURRENTCONNECTIONSTAGE.COMPLETE;
        }





        // Private member functions

        /// <summary>
        /// Starts an asynchronous operation to retrieve the client's message
        /// size.
        /// </summary>
        private void ReceiveClientMsgSize()
        {
            StateObj.Buffer = new byte[NUM_BYTES_MSG_SIZE_DESCRIPTOR];

            // Shared variable StateObj.CurrentStage MUST NOT be modified after starting asynchronous operation
            StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.WAITING_RECEIVE_SIZE;
            myClientSocket.BeginReceive(StateObj.Buffer, 0, NUM_BYTES_MSG_SIZE_DESCRIPTOR, SocketFlags.None, new AsyncCallback(AsyncReadSizeCallback), StateObj);
        }
        /// <summary>
        /// The operation to perform when the ClientManager has received the number
        /// of bytes of the client's request
        /// </summary>
        /// 
        /// <param name="Result">
        /// An IAsyncResult that stores state information and any user defined data for this asynchronous operation.
        /// </param>
        private void AsyncReadSizeCallback(IAsyncResult Result)
        {
            try
            {
                int NumBytesReceived = myClientSocket.EndReceive(Result);
                if (NumBytesReceived == NUM_BYTES_MSG_SIZE_DESCRIPTOR)
                {
                    StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.RECEIVE_REQUEST_MSG;
                }
                else
                {
#if DEBUG
                    Debug.WriteLine("ClientManager[AsyncReadSizeCallback]: Invalid number of bytes received");
#endif
                    StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.ERROR;
                }
            }
            catch (SocketException E)
            {
#if DEBUG
                Debug.WriteLine("ClientManager[AsyncReadSizeCallback]: Error occurred trying to access the socket");
                Debug.WriteLine("ClientManager[AsyncReadSizeCallback]: {0}", E.Message);
#endif
                StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.ERROR;
            }
            catch (ObjectDisposedException E)
            {
#if DEBUG
                Debug.WriteLine("ClientManager[AsyncReadSizeCallback]: The socket has been closed");
                Debug.WriteLine("ClientManager[AsyncReadSizeCallback]: {0}", E.Message);
#endif
                StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.ERROR;
            }
        }




        /// <summary>
        /// The operation to perform in order to receive the request from the client
        /// </summary>
        private void ReceiveClientMsg()
        {
            StateObj.TransmissionSize = 0;
            for (int i = 0; i < NUM_BYTES_MSG_SIZE_DESCRIPTOR; i++)
            {
                int ShiftLen = 8 * (NUM_BYTES_MSG_SIZE_DESCRIPTOR - i - 1);
                StateObj.TransmissionSize += (int)((0xFFFFFFFF & StateObj.Buffer[i]) << ShiftLen);
            }
            StateObj.Buffer = new byte[StateObj.TransmissionSize];

            // Shared variable StateObj.CurrentStage MUST NOT be modified after starting asynchronous operation
            StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.WAITING_RECEIVE_MSG;
            myClientSocket.BeginReceive(StateObj.Buffer, 0, StateObj.TransmissionSize, SocketFlags.None, new AsyncCallback(AsyncReadMsgCallback), StateObj);
        }
        /// <summary>
        /// The operation to perform when the ClientManager has received the request
        /// from the client
        /// </summary>
        /// 
        /// <param name="Result">
        /// An IAsyncResult that stores state information and any user defined data for this asynchronous operation.
        /// </param>
        private void AsyncReadMsgCallback(IAsyncResult Result)
        {
            try
            {
                int NumBytesReceived = myClientSocket.EndReceive(Result);
                if (NumBytesReceived >= StateObj.TransmissionSize)
                {
                    StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.LOADING_RESPONSE_IN_BUFFER;
                }
                else
                {
                    StateObj.TransmittedBytes = NumBytesReceived;
                    StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.RETRYING_RECEIVE_REQUEST_MSG;
                }
            }
            catch (SocketException E)
            {
#if DEBUG
                Debug.WriteLine("ClientManager[AsyncReadMsgCallback]: Error occurred trying to access the socket");
                Debug.WriteLine("ClientManager[AsyncReadMsgCallback]: {0}", E.Message);
#endif
                StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.ERROR;
            }
            catch (ObjectDisposedException E)
            {
#if DEBUG
                Debug.WriteLine("ClientManager[AsyncReadMsgCallback]: The socket has been closed");
                Debug.WriteLine("ClientManager[AsyncReadMsgCallback]: {0}", E.Message);
#endif
                StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.ERROR;
            }
        }




        /// <summary>
        /// The operation to perform in order to receive more bytes of the request
        /// </summary>
        private void RetryReceiveMsg()
        {
            // Shared variable StateObj.CurrentStage MUST NOT be modified after starting asynchronous operation
            StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.WAITING_RECEIVE_MSG;
            myClientSocket.BeginReceive(StateObj.Buffer,
                                StateObj.TransmittedBytes,
                                StateObj.TransmissionSize - StateObj.TransmittedBytes,
                                SocketFlags.None,
                                new AsyncCallback(AsyncRetryReceiveMsgCallback),
                                StateObj);
        }
        /// <summary>
        /// The operation to perform when the ClientManager has received more bytes
        /// of the request from the client
        /// </summary>
        /// 
        /// <param name="Result">
        /// An IAsyncResult that stores state information and any user defined data for this asynchronous operation.
        /// </param>
        private void AsyncRetryReceiveMsgCallback(IAsyncResult Result)
        {
            try
            {
                int NumBytesReceived = myClientSocket.EndReceive(Result);
                StateObj.TransmittedBytes += NumBytesReceived;

                if (StateObj.TransmittedBytes >= StateObj.TransmissionSize)
                {
                    StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.LOADING_RESPONSE_IN_BUFFER;
                }
                else
                {
                    StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.RETRYING_RECEIVE_REQUEST_MSG;
                }
            }
            catch (SocketException E)
            {
#if DEBUG
                Debug.WriteLine("ClientManager[AsyncRetryReceiveResponseCallback]: Error occurred trying to access the socket");
                Debug.WriteLine("ClientManager[AsyncRetryReceiveResponseCallback]: {0}", E.Message);
#endif
                StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.ERROR;
            }
            catch (ObjectDisposedException E)
            {
#if DEBUG
                Debug.WriteLine("ClientManager[AsyncRetryReceiveResponseCallback]: The socket has been closed");
                Debug.WriteLine("ClientManager[AsyncRetryReceiveResponseCallback]: {0}", E.Message);
#endif
                StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.ERROR;
            }
        }




        /// <summary>
        /// The operation to perform in order to load the server's response to
        /// the client.\n
        /// This operation will invoke the callback function to load a custom
        /// response, if provided.
        /// </summary>
        private void LoadServerResponseInBuffer(ref bool TerminationFlag)
        {
            // If the response delegate does not exist or the response delegate
            // returns a null value, use the standard response to send back
            // to the client
            if (ResponseDelegate == null ||
                (StateObj.Buffer = ResponseDelegate(StateObj.Buffer, ref TerminationFlag)) == null)
            {
                StateObj.Buffer = Encoding.ASCII.GetBytes("server_utils");
            }

            StateObj.TransmissionSize = StateObj.Buffer.Length;
            StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.SENDING_RESPONSE_SIZE;
        }




        /// <summary>
        /// The operation to perform in order to send the size of the server's
        /// response to the client.
        /// </summary>
        private void SendResponseMsgSize()
        {
            byte[] TransmissionSize = new byte[NUM_BYTES_MSG_SIZE_DESCRIPTOR];
            for (int i = 0; i < NUM_BYTES_MSG_SIZE_DESCRIPTOR; i++)
            {
                int ShiftLen = 8 * (NUM_BYTES_MSG_SIZE_DESCRIPTOR - i - 1);
                TransmissionSize[i] = (byte)(0xFF & (StateObj.TransmissionSize >> ShiftLen));
            }

            // Shared variable StateObj.CurrentStage MUST NOT be modified after starting asynchronous operation
            StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.WAITING_SEND_SIZE;
            myClientSocket.BeginSend(TransmissionSize, 0, NUM_BYTES_MSG_SIZE_DESCRIPTOR, SocketFlags.None, new AsyncCallback(AsyncSendSizeCallback), StateObj);
        }
        /// <summary>
        /// The operation to perform when the ClientManager has sent the size of the
        /// response to the client
        /// </summary>
        /// 
        /// <param name="Result">
        /// An IAsyncResult that stores state information and any user defined data for this asynchronous operation.
        /// </param>
        private void AsyncSendSizeCallback(IAsyncResult Result)
        {
            try
            {
                int NumBytesTransmitted = myClientSocket.EndSend(Result);
                if (NumBytesTransmitted == NUM_BYTES_MSG_SIZE_DESCRIPTOR)
                {
                    StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.SENDING_RESPONSE_MSG;
                }
                else
                {
#if DEBUG
                    Debug.WriteLine("ClientManager[AsyncSendSizeCallback]: Invalid number of bytes received");
#endif
                    StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.ERROR;
                }
            }
            catch (SocketException E)
            {
#if DEBUG
                Debug.WriteLine("ClientManager[AsyncSendSizeCallback]: Error occurred trying to access the socket");
                Debug.WriteLine("ClientManager[AsyncSendSizeCallback]: {0}", E.Message);
#endif
                StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.ERROR;
            }
            catch (ObjectDisposedException E)
            {
#if DEBUG
                Debug.WriteLine("ClientManager[AsyncSendSizeCallback]: The socket has been closed");
                Debug.WriteLine("ClientManager[AsyncSendSizeCallback]: {0}", E.Message);
#endif
                StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.ERROR;
            }
        }




        /// <summary>
        /// The operation to perform in order to send the response to the
        /// client.
        /// </summary>
        private void SendResponseMsg()
        {
            // Shared variable StateObj.CurrentStage MUST NOT be modified after starting asynchronous operation
            StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.WAITING_SEND_MSG;
            myClientSocket.BeginSend(StateObj.Buffer, 0, StateObj.TransmissionSize, SocketFlags.None, new AsyncCallback(AsyncSendResponseCallback), StateObj);
        }
        /// <summary>
        /// The operation to perform when the ClientManager has sent the
        /// response to the client
        /// </summary>
        /// 
        /// <param name="Result">
        /// An IAsyncResult that stores state information and any user defined data for this asynchronous operation.
        /// </param>
        private void AsyncSendResponseCallback(IAsyncResult Result)
        {
            try
            {
                int NumBytesTransmitted = myClientSocket.EndSend(Result);
                if (NumBytesTransmitted == StateObj.TransmissionSize)
                {
                    StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.COMPLETE;
                }
                else
                {
#if DEBUG
                    Debug.WriteLine("ClientManager[AsyncSendResponseCallback]: Invalid number of bytes received");
#endif
                    StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.ERROR;
                }
            }
            catch (SocketException E)
            {
#if DEBUG
                Debug.WriteLine("ClientManager[AsyncSendResponseCallback]: Error occurred trying to access the socket");
                Debug.WriteLine("ClientManager[AsyncSendResponseCallback]: {0}", E.Message);
#endif
                StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.ERROR;
            }
            catch (ObjectDisposedException E)
            {
#if DEBUG
                Debug.WriteLine("ClientManager[AsyncSendResponseCallback]: The socket has been closed");
                Debug.WriteLine("ClientManager[AsyncSendResponseCallback]: {0}", E.Message);
#endif
                StateObj.CurrentStage = StateObject.CURRENTCONNECTIONSTAGE.ERROR;
            }
        }
    }
}
