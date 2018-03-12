using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;

#if DEBUG
using System.Diagnostics;
#endif

namespace ServerUtils
{
    /// <summary>
    /// A server utility designed to handle multiple clients simultaneously,
    /// allowing for custom handling to be performed for each new client.
    /// </summary>
    public class CoreServer
    {
        /// <summary>
        /// A handler which will be invoked when there is a change in the status of a Thread
        /// </summary>
        public delegate void ThreadStateChangeHandler();

        /// <summary>
        /// A handler for new connections. This handler will be performed on
        /// separate thread from the main thread.
        /// </summary>
        /// 
        /// <param name="ClientSocket">
        /// The Socket responsible for handling connections to the Client
        /// </param>
        /// <param name="TerminationFlag">
        /// A flag indicating that this function should terminate immediately.
        /// This flag should be constantly monitored in order to be notified of
        /// a shutdown request
        /// </param>
        public delegate void ConnectionHandler(Socket ClientSocket, ref bool ShutDownFlag);


        // NETWORK INFORMATION
        /// <summary>
        /// The port number on which the server should operate
        /// </summary>
        private int myServerPortNum;

        /// <summary>
        /// The number of connection requests the server should queue in its
        /// backlog to serve, before starting to reject.
        /// </summary>
        private int myServerBacklogSize;

        /// <summary>
        /// A utility to handle new connections.
        /// </summary>
        private SocketConnectionManager mySocketConnectionManager;

        // THREAD INFORMATION
        /// <summary>
        /// A flag indicating that the server should stop accepting new requests,
        /// but should finish serving its current connections before shutting
        /// down.
        /// </summary>
        private bool SoftServerThreadShutDownFlag;
        /// <summary>
        /// A flag indicating that the server should immediately shut down, halting
        /// all current service operations to all connected clients.
        /// </summary>
        private bool HardServerThreadShutDownFlag;
        /// <summary>
        /// The thread on which the server should operate.
        /// </summary>
        private Thread myServerThread;
        /// <summary>
        /// A utility to run operations on the main thread.
        /// </summary>
        private SynchronizationContext myMainThread;
        /// <summary>
        /// A flag indicating whether the server should be run on the same
        /// thread that started it or should be run on a new thread.
        /// </summary>
        private bool RunServerOnCurrentThread;

        // CALLBACKs
        /// <summary>
        /// The handler to invoke when the server is initialized. This will
        /// be run on the main thread.
        /// </summary>
        private ThreadStateChangeHandler myThreadStartedHandler;

        /// <summary>
        /// The handler to invoke when the server has failed to initialize. This
        /// will be run on the main thread.
        /// </summary>
        private ThreadStateChangeHandler myThreadStartFailureHandler;

        /// <summary>
        /// The handler to invoke when the server is shutting down. This will
        /// be run on the main thread.
        /// </summary>
        private ThreadStateChangeHandler myThreadStoppedHandler;

        /// <summary>
        /// Initilizer for the CoreServer class.
        /// </summary>
        /// 
        /// <param name="ServerPortNum">
        /// The port number on which the server should operate.
        /// </param>
        /// <param name="ServerBacklogSize">
        /// The number of connection requests the server should queue,
        /// before starting to reject.
        /// </param>
        /// <param name="RunOnCurrentThread">
        /// True if the server should be run on the current thread.
        /// False if a new thread should be started to run the server.
        /// </param>
        public CoreServer(int ServerPortNum, int ServerBacklogSize, bool RunOnCurrentThread)
        {
            myServerPortNum = ServerPortNum;
            myServerBacklogSize = ServerBacklogSize;
            mySocketConnectionManager = new SocketConnectionManager();

            SoftServerThreadShutDownFlag = true;
            HardServerThreadShutDownFlag = true;
            myServerThread = null;
            myMainThread = SynchronizationContext.Current;
            if (myMainThread == null)
            {
                myMainThread = new SynchronizationContext();
            }
            RunServerOnCurrentThread = RunOnCurrentThread;

            myThreadStartedHandler = null;
            myThreadStartFailureHandler = null;
            myThreadStoppedHandler = null;
        }
        ~CoreServer()
        {
            HardServerThreadShutDownFlag = true;
        }

        // Properties

        /// <summary>
        /// The handler to be invoked when the Server's main thread
        /// is initialized.
        /// This handler will be called on the main thread.
        /// </summary>
        public ThreadStateChangeHandler ThreadStartedHandler
        {
            set
            {
                myThreadStartedHandler = value;
            }
        }

        /// <summary>
        /// The handler to be invoked when the Server's main thread
        /// has failed to initialize.
        /// This handler will be called on the main thread.
        /// </summary>
        public ThreadStateChangeHandler ThreadStartFailureHandler
        {
            set
            {
                myThreadStartFailureHandler = value;
            }
        }

        /// <summary>
        /// The handler to be invoked when the Server's main thread
        /// is halted.
        /// This handler will be called on the main thread.
        /// </summary>
        public ThreadStateChangeHandler ThreadStoppedHandler
        {
            set
            {
                myThreadStoppedHandler = value;
            }
        }

        /// <summary>
        /// The handler to be invoked when a new connection hs received.
        /// This handler will be invoked on a new thread
        /// </summary>
        public ConnectionHandler NewConnectionHandler
        {
            set
            {
                mySocketConnectionManager.NewConnectionHandler = value;
            }
        }

        /// <summary>
        /// A flag indicating whether the server is active or halted.
        /// </summary>
        public bool ServerActive
        {
            get
            {
                return myServerThread != null && myServerThread.IsAlive;
            }
        }




        // PUBLIC MEMBER FUNCTIONS

        /// <summary>
        /// Will reset the port number for the server to listen on
        /// if the server is stopped.
        /// </summary>
        /// 
        /// <param name="PortNumber">
        /// The port number for the server fo listen on.
        /// </param>
        /// 
        /// <returns>
        /// Will return true if the port number was successfully reset.
        /// Will return false if the operation failed.
        /// </returns>
        public bool SetPortNumber(int PortNumber)
        {
            if (myServerThread == null || !myServerThread.IsAlive)
            {
                myServerPortNum = PortNumber;
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Will reset the backlog size for the server if the server
        /// is stopped.
        /// </summary>
        /// 
        /// <param name="BacklogSize">
        /// The new backlog size for the server.
        /// </param>
        /// 
        /// <returns>
        /// Will return true if the backlog size was successfully reset.
        /// Will return false if the operation failed.
        /// </returns>
        public bool SetBacklogSize(int BacklogSize)
        {
            if (myServerThread == null || !myServerThread.IsAlive)
            {
                myServerBacklogSize = BacklogSize;
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Will send a start request to the server
        /// </summary>
        /// 
        /// <returns>
        /// True if the start request was successfully sent.
        /// False if the request was unsuccessfully sent
        /// </returns>
        public bool StartServer()
        {
            if (myServerThread == null || !myServerThread.IsAlive)
            {
                // Reset shutdown flags for server and client handler
                SoftServerThreadShutDownFlag = false;
                HardServerThreadShutDownFlag = false;
                mySocketConnectionManager.ResetShutDownFlag();

                if (RunServerOnCurrentThread)
                {
                    ServerOperation();
                }
                else
                {
                    // Create server thread
                    myServerThread = new Thread(ServerOperation);
                    myServerThread.Name = "ServerThread";
                    myServerThread.Start();
                }

                return true;
            }
            else
            {
#if DEBUG
                Debug.WriteLine("CoreServer[StartServer]: Server thread not reset");
#endif
                return false;
            }
        }

        /// <summary>
        /// Will signal the server to stop receiving future connections,
        /// but finish all current operations before completely halting
        /// </summary>
        public void SoftStopServer()
        {
            SoftServerThreadShutDownFlag = true;
        }

        /// <summary>
        /// Will signal the server to halt all procedures immediately
        /// </summary>
        public void HardStopServer()
        {
            HardServerThreadShutDownFlag = true;
        }




        // PRIVATE MEMBER FUNCTIONS

        /// <summary>
        /// The core server operation to invoke
        /// </summary>
        private void ServerOperation()
        {
            Socket ServerSocket = InstantiateServer();
            if (ServerSocket != null)
            {
                InvokeServerStatusHandlerMainThread(myThreadStartedHandler);
                while (!HardServerThreadShutDownFlag && !SoftServerThreadShutDownFlag)
                {
                    mySocketConnectionManager.ClearFinishedClientThreads();
                    Socket ClientSocket = AcceptNextClientSocket(ref ServerSocket);
                    if (ClientSocket != null)
                    {
                        mySocketConnectionManager.InvokeNewClientHandler(ClientSocket);
                    }
                }
                ServerSocket.Dispose();
                while (mySocketConnectionManager.AreThreadsActive) { }
                mySocketConnectionManager.SetShutDownFlag();
                InvokeServerStatusHandlerMainThread(myThreadStoppedHandler);
            }
            else
            {
                InvokeServerStatusHandlerMainThread(myThreadStartFailureHandler);
            }
        }


        // SERVER OPERATIONS HELPER FUNCTIONS
        /// <summary>
        /// Will create a new server socket with the specified port number
        /// </summary>
        /// 
        /// <returns>
        /// The newly create server socket, or null if an error was
        /// encountered during creation
        /// </returns>
        private Socket InstantiateServer()
        {
            try
            {
                IPEndPoint EndPoint = new IPEndPoint(new IPAddress(0), myServerPortNum);
                Socket ServerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                ServerSocket.Blocking = false;
                ServerSocket.Bind(EndPoint);
                ServerSocket.Listen(myServerBacklogSize);

                return ServerSocket;
            }
            catch (Exception e)
            {
#if DEBUG
                Debug.WriteLine("CoreServer[InstantiateServer]: {0}", e.Message);
#endif
                return null;
            }
        }

        /// <summary>
        /// Accepts the next available client
        /// </summary>
        /// 
        /// <param name="ServerSocket">
        /// An active socket listening
        /// for connections
        /// </param>
        /// 
        /// <returns>
        /// The next queued client, if available, or null,
        /// if no client is available
        /// </returns>
        private Socket AcceptNextClientSocket(ref Socket ServerSocket)
        {
            try
            {
                Socket ClientSocket = ServerSocket.Accept();
                return ClientSocket;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        /// <summary>
        /// Invokes a registered callback for a ThreadStateChange event.\n
        /// Performs the operation on the Main Thread
        /// </summary>
        /// 
        /// <param name="Handler">
        /// The ThreadStateChangeHandler to invoke
        /// </param>
        private void InvokeServerStatusHandlerMainThread(ThreadStateChangeHandler Handler)
        {
            if (Handler != null)
            {
                try
                {
                    myMainThread.Send((object sender) =>
                    {
                        Handler();
                    }, null);
                }
#if DEBUG
                catch (Exception e)
                {
                    Debug.WriteLine("CoreServer[InvokeServerStatusHandlerMainThread]: " + e.Message);
                }
#else
                catch (Exception) { }
#endif
            }
        }
    }
}
