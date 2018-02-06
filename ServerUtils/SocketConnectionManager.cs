using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;

namespace ServerUtils
{
    /// <summary>
    /// A connection handler for the CoreServer utility.
    /// This handler will create new threads for each new connection
    /// and monitor each service thread until completed.
    /// </summary>
    internal class SocketConnectionManager
    {
        // Client flags
        /// <summary>
        /// A flag signaling that all service operations should
        /// be shut down immediately.
        /// </summary>
        private bool HardShutDownFlag;

        // Callbacks
        /// <summary>
        /// A custom callback method for each service thread to invoke when
        /// a new connection is received.
        /// </summary>
        private CoreServer.ConnectionHandler myConnectionHandler;

        // Threads
        /// <summary>
        /// A list of currently running service threads. This list
        /// is continuously monitored, and any inactive service threads
        /// are  removed.
        /// </summary>
        List<Thread> ClientThreads;

        /// <summary>
        /// Initiliazer for the SocketConnectionHandler class.
        /// </summary>
        internal SocketConnectionManager()
        {
            HardShutDownFlag = true;
            myConnectionHandler = null;
            ClientThreads = new List<Thread>();
        }


        // PROPERTIES
        /// <summary>
        /// Flag for whether any threads are still
        /// actively servicing clients
        /// </summary>
        internal bool AreThreadsActive
        {
            get
            {
                ClearFinishedClientThreads();
                return ClientThreads.Count > 0;
            }
        }

        /// <summary>
        /// A handler for a new connection
        /// </summary>
        internal CoreServer.ConnectionHandler NewConnectionHandler
        {
            set
            {
                myConnectionHandler = value;
            }
        }





        
        // MEMBER FUNCTIONS
        /// <summary>
        /// Resets the shutdown flag
        /// </summary>
        internal void ResetShutDownFlag()
        {
            HardShutDownFlag = false;
        }

        /// <summary>
        /// Signals the shutdown flag to be set to signal all clients
        /// to stop all operations immediately
        /// </summary>
        internal void SetShutDownFlag()
        {
            HardShutDownFlag = true;
        }

        /// <summary>
        /// Creates a new Thread, which will invoke the registered callback
        /// for a new connection event, and will add this thread to the list
        /// of ClientThreads
        /// </summary>
        /// 
        /// <param name="ClientSocket">
        /// The created socket to  handle the connection with the client
        /// </param>
        internal void InvokeNewClientHandler(Socket ClientSocket)
        {
            Thread ConnectionThread = new Thread(ConnectionHandlerWrapper);
            ConnectionThread.Start(ClientSocket);
            ClientThreads.Add(ConnectionThread);
        }
        private void ConnectionHandlerWrapper(Object Parameter)
        {
            Socket ClientSocket = (Socket)Parameter;
            if (myConnectionHandler != null)
            {
                myConnectionHandler(ClientSocket, ref HardShutDownFlag);
            }
            ClientSocket.Dispose();
        }

        /// <summary>
        /// Will remove all Threads from the list of Threads that are no
        /// longer running
        /// </summary>
        internal void ClearFinishedClientThreads()
        {
            for (int i = ClientThreads.Count - 1; i >= 0; i--)
            {
                if (!ClientThreads.ElementAt(i).IsAlive)
                {
                    ClientThreads.RemoveAt(i);
                }
            }
        }
    }
}
