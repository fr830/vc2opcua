﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace vc2opcua
{

    public class Server
    {
        OpcUaServer server;
        Task status;
        DateTime lastEventTime;
        int serverRunTime = Timeout.Infinite;
        static bool autoAccept = false;
        static ExitCode exitCode;

        public Thread ServerThread;

        private static readonly log4net.ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public Server(bool _autoAccept, int _stopTimeout)
        {
            autoAccept = _autoAccept;
            serverRunTime = _stopTimeout == 0 ? Timeout.Infinite : _stopTimeout * 1000;
        }

        public void Run()
        {

            try
            {
                exitCode = ExitCode.ErrorServerNotStarted;
                // Start the server
                ServerTask().Wait();
                logger.Info("[vc2opcua] Server started");
                exitCode = ExitCode.ErrorServerRunning;
            }
            catch (Exception ex)
            {
                Utils.Trace("ServiceResultException:" + ex.Message);
                logger.Error(String.Format("[vc2opcua] {0}",ex.ToString()));
                exitCode = ExitCode.ErrorServerException;
                return;
            }
        }

        public void Stop()
        {
            using (OpcUaServer _server = server)
            {
                // Stop status thread
                server = null;
                status.Wait();
                // Stop server and dispose
                _server.Stop();
                exitCode = ExitCode.Ok;
            }
        }

        public static ExitCode ExitCode { get => exitCode; }
        
        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = autoAccept;
                if (autoAccept)
                {
                    Debug.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
                }
                else
                {
                    Debug.WriteLine("Rejected Certificate: {0}", e.Certificate.Subject);
                }
            }
        }
        
        private async Task ServerTask()
        {
            //ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            ApplicationInstance application = new ApplicationInstance
            {
                ApplicationName = "Vc2OpcUa Server",
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = "Vc2OpcUaServer"
            };

            // load the application configuration.
            ApplicationConfiguration config = await application.LoadApplicationConfiguration(false);
            
            // check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(false, 0);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            if (!config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }

            // start the server.
            server = new OpcUaServer();
            await application.Start(server);

            // start the status thread
            status = Task.Run(new System.Action(StatusThread));

            // print notification on session events
            server.CurrentInstance.SessionManager.SessionActivated += EventStatus;
            server.CurrentInstance.SessionManager.SessionClosing += EventStatus;
            server.CurrentInstance.SessionManager.SessionCreated += EventStatus;

        }

        private void EventStatus(Session session, SessionEventReason reason)
        {
            lastEventTime = DateTime.UtcNow;
            PrintSessionStatus(session, reason.ToString());
        }

        void PrintSessionStatus(Session session, string reason, bool lastContact = false)
        {
            lock (session.DiagnosticsLock)
            {
                string item = String.Format("{0,9}:{1,20}:", reason, session.SessionDiagnostics.SessionName);
                if (lastContact)
                {
                    item += String.Format("Last Event:{0:HH:mm:ss}", session.SessionDiagnostics.ClientLastContactTime.ToLocalTime());
                }
                else
                {
                    if (session.Identity != null)
                    {
                        item += String.Format(":{0,20}", session.Identity.DisplayName);
                    }
                    item += String.Format(":{0}", session.Id);
                }
                Debug.WriteLine(item);
            }
        }

        private async void StatusThread()
        {
            while (server != null)
            {
                if (DateTime.UtcNow - lastEventTime > TimeSpan.FromMilliseconds(6000))
                {
                    IList<Session> sessions = server.CurrentInstance.SessionManager.GetSessions();
                    for (int ii = 0; ii < sessions.Count; ii++)
                    {
                        Session session = sessions[ii];
                        PrintSessionStatus(session, "-Status-", true);
                    }
                    lastEventTime = DateTime.UtcNow;
                }
                await Task.Delay(1000);
            }
        }
    }

    public enum ExitCode : int
    {
        Ok = 0,
        ErrorServerNotStarted = 0x80,
        ErrorServerRunning = 0x81,
        ErrorServerException = 0x82,
        ErrorInvalidCommandLine = 0x100
    };

    /// <summary>
    /// A class which implements an instance of a UA server.
    /// </summary>
    public partial class OpcUaServer : StandardServer
    {
        private static readonly log4net.ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        #region Overridden Methods
        /// <summary>
        /// Initializes the server before it starts up.
        /// </summary>
        /// <remarks>
        /// This method is called before any startup processing occurs. The sub-class may update the 
        /// configuration object or do any other application specific startup tasks.
        /// </remarks>
        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            Debug.WriteLine("The server is starting.");

            base.OnServerStarting(configuration);
        }

        /// <summary>
        /// Cleans up before the server shuts down.
        /// </summary>
        /// <remarks>
        /// This method is called before any shutdown processing occurs.
        /// </remarks>
        protected override void OnServerStopping()
        {
            Debug.WriteLine("The Server is stopping.");

            base.OnServerStopping();
        }

        /// <summary>
        /// Creates the node managers for the server.
        /// </summary>
        /// <remarks>
        /// This method allows the sub-class create any additional node managers which it uses. The SDK
        /// always creates a CoreNodeManager which handles the built-in nodes defined by the specification.
        /// Any additional NodeManagers are expected to handle application specific nodes.
        /// 
        /// Applications with small address spaces do not need to create their own NodeManagers and can add any
        /// application specific nodes to the CoreNodeManager. Applications should use custom NodeManagers when
        /// the structure of the address space is stored in another system or when the address space is too large
        /// to keep in memory.
        /// </remarks>
        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            Debug.WriteLine("Creating the Node Managers.");

            List<INodeManager> nodeManagers = new List<INodeManager>();

            Vc2OpcUaBridge bridge = new Vc2OpcUaBridge(server, configuration);
            // create the custom node manager.
            nodeManagers.Add(bridge.nodeManager);

            // create master node manager.
            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
        }

        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        /// <remarks>
        /// These properties are exposed by the server but cannot be changed by administrators.
        /// </remarks>
        protected override ServerProperties LoadServerProperties()
        {
            ServerProperties properties = new ServerProperties
            {
                ManufacturerName = "OPC Foundation",
                ProductName = "OPC UA SDK Samples",
                ProductUri = "http://opcfoundation.org/UA/Samples/v1.0",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp()
            };

            return properties;
        }

        /// <summary>
        /// Initializes the address space after the NodeManagers have started.
        /// </summary>
        /// <remarks>
        /// This method can be used to create any initialization that requires access to node managers.
        /// </remarks>
        protected override void OnNodeManagerStarted(IServerInternal server)
        {
            logger.Info("[vc2opcua] Node managers have started");

            // allow base class processing to happen first.
            base.OnNodeManagerStarted(server);
        }
        #endregion
    }

}