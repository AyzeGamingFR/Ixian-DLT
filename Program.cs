﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using DLT;
using DLT.Meta;
using System.Text;
using DLT.Network;
using System.Threading;
using System.Diagnostics;
using System.IO;
using IXICore;
using System.Runtime.InteropServices;
using System.Security.Permissions;

namespace DLTNode
{
    class Program
    {

        // STD_INPUT_HANDLE (DWORD): -10 is the standard input device.
        const int STD_INPUT_HANDLE = -10;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll")]
        static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        [DllImport("Kernel32")]
        static extern bool SetConsoleCtrlHandler(HandlerRoutine Handler, bool Add);

        delegate bool HandlerRoutine(CtrlTypes CtrlType);

        enum CtrlTypes
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT,
            CTRL_CLOSE_EVENT,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT
        }

        static void CheckRequiredFiles()
        {
            string[] critical_dlls =
            {
                "BouncyCastle.Crypto.dll",
                "FluentCommandLineParser.dll",
                "Newtonsoft.Json.dll",
                "Open.Nat.dll",
                "SQLite-net.dll",
                "SQLitePCLRaw.batteries_green.dll",
                "SQLitePCLRaw.batteries_v2.dll",
                "SQLitePCLRaw.core.dll",
                "SQLitePCLRaw.provider.e_sqlite3.dll",
                "System.Console.dll",
                "System.Reflection.TypeExtensions.dll",
                "x64" + Path.DirectorySeparatorChar + "e_sqlite3.dll"
            };
            foreach(string critical_dll in critical_dlls)
            {
                if(!File.Exists(critical_dll))
                {
                    Logging.error(String.Format("Missing '{0}' in the program folder. Possibly the IXIAN archive was corrupted or incorrectly installed. Please re-download the archive from https://www.ixian.io!", critical_dll));
                    Logging.info("Press ENTER to exit.");
                    Console.ReadLine();
                    Environment.Exit(-1);
                }
            }

            // Special case for argon
            if (!File.Exists("libargon2.dll") && !File.Exists("libargon2.so") && !File.Exists("libargon2.dylib"))
            {
                Logging.error(String.Format("Missing '{0}' in the program folder. Possibly the IXIAN archive was corrupted or incorrectly installed. Please re-download the archive from https://www.ixian.io!", "libargon2"));
                Logging.info("Press ENTER to exit.");
                Console.ReadLine();
                Environment.Exit(-1);
            }

        }
        static void CheckVCRedist()
        {
            object installed_vc_redist = Microsoft.Win32.Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x64", "Installed", 0);
            object installed_vc_redist_debug = Microsoft.Win32.Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\debug\\x64", "Installed", 0);
            bool success = false;
            if ((installed_vc_redist is int && (int)installed_vc_redist > 0) || (installed_vc_redist_debug is int && (int)installed_vc_redist_debug > 0))
            {
                Logging.info("Visual C++ 2017 (v141) redistributable is already installed.");
                success = true;
            }
            else
            {
                if (!File.Exists("vc_redist.x64.exe"))
                {
                    Logging.warn("The VC++2017 redistributable file is not found. Please download the v141 version of the Visual C++ 2017 redistributable and install it manually!");
                    Logging.flush();
                    Console.WriteLine("You can download it from this URL:");
                    Console.WriteLine("https://visualstudio.microsoft.com/downloads/");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine();
                    Console.WriteLine();
                    Console.WriteLine();
                    Console.WriteLine("NOTICE: In order to run this IXIAN node, Visual Studio 2017 Redistributable (v141) must be installed.");
                    Console.WriteLine("This can be done automatically by IXIAN, or, you can install it manually from this URL:");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("https://visualstudio.microsoft.com/downloads/");
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine("The installer may open a UAC (User Account Control) prompt. Please verify that the executable is signed by Microsoft Corporation before allowing it to install!");
                    Console.ResetColor();
                    Console.WriteLine();
                    Console.WriteLine("Automatically install Visual C++ 2017 redistributable? (Y/N): ");
                    ConsoleKeyInfo k = Console.ReadKey();
                    Console.WriteLine();
                    Console.WriteLine();
                    if (k.Key == ConsoleKey.Y)
                    {
                        Logging.info("Installing Visual C++ 2017 (v141) redistributable...");
                        ProcessStartInfo installer = new ProcessStartInfo("vc_redist.x64.exe");
                        installer.Arguments = "/install /passive /norestart";
                        installer.LoadUserProfile = false;
                        installer.RedirectStandardError = true;
                        installer.RedirectStandardInput = true;
                        installer.RedirectStandardOutput = true;
                        installer.UseShellExecute = false;
                        Logging.info("Starting installer. Please allow up to one minute for installation...");
                        Process p = Process.Start(installer);
                        while (!p.HasExited)
                        {
                            if (!p.WaitForExit(60000))
                            {
                                Logging.info("The install process seems to be stuck. Terminate? (Y/N): ");
                                k = Console.ReadKey();
                                if (k.Key == ConsoleKey.Y)
                                {
                                    Logging.warn("Terminating installer process...");
                                    p.Kill();
                                    Logging.warn(String.Format("Process output: {0}", p.StandardOutput.ReadToEnd()));
                                    Logging.warn(String.Format("Process error output: {0}", p.StandardError.ReadToEnd()));
                                }
                            }
                        }
                        installed_vc_redist = Microsoft.Win32.Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x64", "Installed", 0);
                        installed_vc_redist_debug = Microsoft.Win32.Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\debug\\x64", "Installed", 0);
                        if ((installed_vc_redist is int && (int)installed_vc_redist > 0) || (installed_vc_redist_debug is int && (int)installed_vc_redist_debug > 0))
                        {
                            Logging.info("Visual C++ 2017 (v141) redistributable has installed successfully.");
                            success = true;
                        }
                        else
                        {
                            Logging.info("Visual C++ 2017 has failed to install. Please review the error text (if any) and install manually:");
                            Logging.warn(String.Format("Process exit code: {0}.", p.ExitCode));
                            Logging.warn(String.Format("Process output: {0}", p.StandardOutput.ReadToEnd()));
                            Logging.warn(String.Format("Process error output: {0}", p.StandardError.ReadToEnd()));
                        }
                    }
                }
            }
            if (!success)
            {
                Logging.info("IXIAN requires the Visual Studio 2017 runtime for normal operation. Please ensure it is installed and then restart the program!");
                Logging.info("Press ENTER to exit.");
                Console.ReadLine();
                Environment.Exit(-1);
            }
        }

        [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.ControlAppDomain)]
        static void installUnhandledExceptionHandler()
        {
            System.AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Logging.error(String.Format("Exception was triggered and not handled. Please send this log to the Ixian developers!"));
            Logging.error(e.ExceptionObject.ToString());
        }

        private static System.Timers.Timer mainLoopTimer;

        public static bool noStart = false;

        // Handle Windows OS-specific calls
        static void prepareWindowsConsole()
        {
            // Ignore if we're on Mono
            if (IXICore.Platform.onMono())
                return;

            installUnhandledExceptionHandler();

            IntPtr consoleHandle = GetStdHandle(STD_INPUT_HANDLE);

            // get current console mode
            uint consoleMode;
            if (!GetConsoleMode(consoleHandle, out consoleMode))
            {
                // ERROR: Unable to get console mode.
                return;
            }

            // Clear the quick edit bit in the mode flags
            consoleMode &= ~(uint)0x0040; // quick edit

            // set the new mode
            if (!SetConsoleMode(consoleHandle, consoleMode))
            {
                // ERROR: Unable to set console mode
            }

            // Hook a handler for force close
            SetConsoleCtrlHandler(new HandlerRoutine(HandleConsoleClose), true);
        }

        static void Main(string[] args)
        {            
            Console.Clear();

            prepareWindowsConsole();

            // Start logging
            Logging.start();

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e) {
                e.Cancel = true;
                Node.apiServer.forceShutdown = true;
            };

            // For testing only. Run any experiments here as to not affect the infrastructure.
            // Failure of tests will result in termination of the dlt instance.
            /*if(runTests(args) == false)
            {
                return;
            }*/

            onStart(args);

            if(Node.apiServer != null)
            { 
                while (Node.apiServer.forceShutdown == false)
                {
                    Thread.Sleep(1000);
                }
            }

            if (noStart == false)
            {
                Console.WriteLine("Ixian DLT is stopping...");
            }
            onStop();

        }

        static void onStart(string[] args)
        {
            bool verboseConsoleOutputSetting = Config.verboseConsoleOutput;
            Config.verboseConsoleOutput = true;

            Console.WriteLine(string.Format("IXIAN DLT {0}", Config.version));

            // Check for critical files in the exe dir
            CheckRequiredFiles();

            // Read configuration from command line
            Config.readFromCommandLine(args);

            // Benchmark keys is a special case, because it will not start any part of the node.
            if (Config.benchmarkKeys > 0)
            {
                if (Config.benchmarkKeys != 1024 && Config.benchmarkKeys != 2048 && Config.benchmarkKeys != 4096)
                {
                    Logging.error(String.Format("Invalid key bit length: {0}. Allowed values are 1024, 2048 or 4096!", Config.benchmarkKeys));
                }
                else
                {
                    IXICore.CryptoKey.KeyDerivation.BenchmarkKeyGeneration(10000, Config.benchmarkKeys, "bench_keys.out");
                }
                noStart = true;
            }

            // Debugging option: generate wallet only and set password from commandline
            if(Config.generateWalletOnly)
            {
                noStart = true;
                if (Config.isTestNet)
                {
                    if (File.Exists(Config.walletFile))
                    {
                        Logging.error(String.Format("Wallet file {0} already exists. Cowardly refusing to overwrite!", Config.walletFile));
                    }
                    else
                    {
                        Logging.info("Generating a new wallet.");
                        CryptoManager.initLib();
                        WalletStorage wst = new WalletStorage(Config.walletFile);
                        wst.writeWallet(Config.dangerCommandlinePasswordCleartextUnsafe);
                    }
                } else
                {
                    // the main reason we don't allow stuff like 'generateWallet' in mainnet, is because the resulting wallet will have to:
                    //  a. Have an empty password (easy to steal via a misconfifured file sharing program)
                    //  b. Have a predefined password (ditto)
                    //  c. Require password on the command line, which usually leads to people making things like 'start.bat' with cleartext passwords, thus defeating
                    //     wallet encryption
                    // However, it is useful to be able to spin up a lot of nodes automatically and know their wallet addresses, therefore this sort of behavior is allowed
                    //   for testnet.
                    Logging.error("Due to security reasons, the 'generateWallet' option is only valid when starting a TestNet node!");
                }
            }

            if (noStart)
            {
                Thread.Sleep(1000);
                return;
            }

            // Set the logging options
            Logging.setOptions(Config.maxLogSize, Config.maxLogCount);

            Logging.info(string.Format("Starting IXIAN DLT {0}", Config.version));
            Logging.flush();

            // Check for the right vc++ redist for the argon miner
            // Ignore if we're on Mono
            if (IXICore.Platform.onMono() == false)
            {
                CheckVCRedist();
            }

            // Log the parameters to notice any changes
            Logging.info(String.Format("Mainnet: {0}", !Config.isTestNet));

            if(Config.workerOnly)
                Logging.info("Miner: worker-only");
            else
                Logging.info(String.Format("Miner: {0}", !Config.disableMiner));

            Logging.info(String.Format("Server Port: {0}", Config.serverPort));
            Logging.info(String.Format("API Port: {0}", Config.apiPort));
            Logging.info(String.Format("Wallet File: {0}", Config.walletFile));

            // Initialize the crypto manager
            CryptoManager.initLib();

            // Initialize the node
            Node.init();

            if (noStart)
            {
                Thread.Sleep(1000);
                return;
            }

            // Start the actual DLT node
            Node.start(verboseConsoleOutputSetting);


            // Setup a timer to handle routine updates
            mainLoopTimer = new System.Timers.Timer(1000);
            mainLoopTimer.Elapsed += new ElapsedEventHandler(onUpdate);
            mainLoopTimer.Start();

            if (Config.verboseConsoleOutput)
                Console.WriteLine("-----------\nPress Ctrl-C or use the /shutdown API to stop the DLT process at any time.\n");

        }

        static void onUpdate(object source, ElapsedEventArgs e)
        {
            if (Console.KeyAvailable)
            {
                ConsoleKeyInfo key = Console.ReadKey();
                /*if(key.Key == ConsoleKey.B)
                {
                    Node.forceNextBlock = true;
                }*/
                if (key.Key == ConsoleKey.W)
                {
                    string ws_checksum = Crypto.hashToString(Node.walletState.calculateWalletStateChecksum());
                    Logging.info(String.Format("WalletState checksum: ({0} wallets, {1} snapshots) : {2}",
                        Node.walletState.numWallets, Node.walletState.hasSnapshot, ws_checksum));
                }
                else if (key.Key == ConsoleKey.V)
                {
                    Config.verboseConsoleOutput = !Config.verboseConsoleOutput;
                    Logging.consoleOutput = Config.verboseConsoleOutput;
                    Console.CursorVisible = Config.verboseConsoleOutput;
                    if (Config.verboseConsoleOutput == false)
                        Node.statsConsoleScreen.clearScreen();
                }               
                else if (key.Key == ConsoleKey.H)
                {
                    ulong[] temp = new ulong[ProtocolMessage.recvByteHist.Length];
                    lock (ProtocolMessage.recvByteHist)
                    {
                        ProtocolMessage.recvByteHist.CopyTo(temp, 0);
                    }
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("==================RECEIVED BYTES HISTOGRAM:===================");
                    for (int i = 0; i < temp.Length; i++)
                    {
                        Console.WriteLine(String.Format("[{0}]: {1}", i, temp[i]));
                    }
                    Console.WriteLine("==================RECEIVED BYTES HISTOGRAM:===================");
                    Console.ResetColor();
                }
                else if(key.Key == ConsoleKey.Escape)
                {
                    Node.apiServer.forceShutdown = true;
                }
                else if (key.Key == ConsoleKey.M)
                {
                    if (Node.miner != null)
                        Node.miner.pause = !Node.miner.pause;

                    if (Config.verboseConsoleOutput == false)
                        Node.statsConsoleScreen.clearScreen();
                }
                else if (key.Key == ConsoleKey.N)
                {
                    if (Node.miner != null)
                        Node.miner.forceSearchForBlock();
                }
                else if (key.Key == ConsoleKey.B)
                {
                    if (Node.miner != null)
                    {
                        // Adjust the search mode
                        Node.miner.searchMode++;
                        if (Node.miner.searchMode > BlockSearchMode.random)
                            Node.miner.searchMode = BlockSearchMode.lowestDifficulty;

                        // Force a new block search using the newly chosen method
                        Node.miner.forceSearchForBlock();
                    }
                }

            }
            if (Node.update() == false)
            {
                Node.apiServer.forceShutdown = true;
            }
        }

        static void onStop()
        {
            if (mainLoopTimer != null)
            {
                mainLoopTimer.Stop();
            }

            if (noStart == false)
            {
                // Stop the DLT
                Node.stop();
            }

            // Stop logging
            Logging.flush();
            Logging.stop();

            if (noStart == false)
            {
                Console.WriteLine("");
                Console.WriteLine("Ixian DLT Node stopped.");
            }
        }

        static bool HandleConsoleClose(CtrlTypes type)
        {
            switch(type)
            {
                case CtrlTypes.CTRL_C_EVENT:
                case CtrlTypes.CTRL_BREAK_EVENT:
                    return true;
                case CtrlTypes.CTRL_CLOSE_EVENT:
                case CtrlTypes.CTRL_LOGOFF_EVENT:
                case CtrlTypes.CTRL_SHUTDOWN_EVENT:
                    Console.WriteLine();
                    Logging.info("Application is being closed! Shutting down!");
                    Logging.flush();
                    noStart = true;
                    if (Node.apiServer != null)
                    {
                        Node.apiServer.forceShutdown = true;
                    }
                    // Wait (max 5 seconds) for everything to die
                    DateTime waitStart = DateTime.Now;
                    while(true)
                    {
                        if(Process.GetCurrentProcess().Threads.Count > 1)
                        {
                            Thread.Sleep(50);
                        } else
                        {
                            Console.WriteLine(String.Format("Graceful shutdown achieved in {0} seconds.", (DateTime.Now - waitStart).TotalSeconds));
                            break;
                        }
                        if((DateTime.Now - waitStart).TotalSeconds > 30)
                        {
                            Console.WriteLine("Unable to gracefully shutdown. Aborting. Threads that are still alive: ");
                            foreach(Thread t in Process.GetCurrentProcess().Threads)
                            {
                                Console.WriteLine(String.Format("Thread {0}: {1}.", t.ManagedThreadId, t.Name));
                            }
                            break;
                        }
                    }
                    return true;
            }
            return true;
        }


        static bool runTests(string[] args)
        {
            Logging.log(LogSeverity.info, "Running Tests:");

            // Create a crypto lib
            CryptoLib crypto_lib = new CryptoLib(new CryptoLibs.BouncyCastle());
            IxianKeyPair kp = crypto_lib.generateKeys(CoreConfig.defaultRsaKeySize);

            Logging.log(LogSeverity.info, String.Format("Public Key base64: {0}", kp.publicKeyBytes));
            Logging.log(LogSeverity.info, String.Format("Private Key base64: {0}", kp.privateKeyBytes));


            /// ECDSA Signature test
            // Generate a new signature
            byte[] signature = crypto_lib.getSignature(Encoding.UTF8.GetBytes("Hello There!"), kp.publicKeyBytes);
            Logging.log(LogSeverity.info, String.Format("Signature: {0}", signature));

            // Verify the signature
            if(crypto_lib.verifySignature(Encoding.UTF8.GetBytes("Hello There!"), kp.publicKeyBytes, signature))
            {
                Logging.log(LogSeverity.info, "SIGNATURE IS VALID");
            }

            // Try a tamper test
            if (crypto_lib.verifySignature(Encoding.UTF8.GetBytes("Hello Tamper!"), kp.publicKeyBytes, signature))
            {
                Logging.log(LogSeverity.info, "SIGNATURE IS VALID AND MATCHES ORIGINAL TEXT");
            }
            else
            {
                Logging.log(LogSeverity.info, "TAMPERED SIGNATURE OR TEXT");
            }

            // Generate a new signature for the same text
            byte[] signature2 = crypto_lib.getSignature(Encoding.UTF8.GetBytes("Hello There!"), kp.privateKeyBytes);
            Logging.log(LogSeverity.info, String.Format("Signature Again: {0}", signature2));

            // Verify the signature again
            if (crypto_lib.verifySignature(Encoding.UTF8.GetBytes("Hello There!"), kp.publicKeyBytes, signature2))
            {
                Logging.log(LogSeverity.info, "SIGNATURE IS VALID");
            }



            Logging.log(LogSeverity.info, "-------------------------");

            // Generate a mnemonic hash from a 64 character string. If the result is always the same, it works correctly.
            Mnemonic mnemonic_addr = new Mnemonic(Wordlist.English, Encoding.ASCII.GetBytes("hahahahahahahahahahahahahahahahahahahahahahahahahahahahahahahaha"));
            Logging.log(LogSeverity.info, String.Format("Mnemonic Hashing Test: {0}", mnemonic_addr));
            Logging.log(LogSeverity.info, "-------------------------");


            // Create an address from the public key
            Address addr = new Address(kp.publicKeyBytes);
            Logging.log(LogSeverity.info, String.Format("Address generated from public key above: {0}", addr));
            Logging.log(LogSeverity.info, "-------------------------");


            // Testing sqlite wrapper
            var db = new SQLite.SQLiteConnection("storage.dat");

            // Testing internal data structures
            db.CreateTable<Block>();

            Block new_block = new Block();
            db.Insert(new_block);

            IEnumerable<Block> block_list = db.Query<Block>("select * from Block");

            if (block_list.OfType<Block>().Count() > 0)
            {
                Block first_block = block_list.FirstOrDefault();
                Logging.log(LogSeverity.info, String.Format("Stored genesis block num is: {0}", first_block.blockNum));
            }


            Logging.log(LogSeverity.info, "Tests completed successfully.\n\n");

            return true;
        }
    }
}
