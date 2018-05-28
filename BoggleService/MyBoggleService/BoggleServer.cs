// Created by Wei-Tung Tang and Chen-Chia Wang

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CustomNetworking;
using Newtonsoft.Json;

namespace Boggle
{
    /// <summary>
    /// Represents a server that establishes connections with a SSListener.
    /// </summary>
    public class BoggleServer
    {
        private SSListener server;


        static void Main(string[] args)
        {
            new BoggleServer(60000);
            Console.ReadLine();
        }

        private SSListener server;

        /// <summary>
        /// Server constructor used to initialize the SSListener and begin listening to client connection
        /// </summary>
        /// <param name="port"></param>
        public BoggleServer(int port)
        {
            server = new SSListener(port, Encoding.UTF8);
            server.Start();
            server.BeginAcceptSS(ConnectionRequested, server);
        }

        /// <summary>
        /// When a connection is requested, the TcpListener calls this method,
        /// which collects the socket and tells the TcpListener to continue
        /// listening.
        /// </summary>
        /// <param name="result"></param>
        private void ConnectionRequested(SS ss, object payload)
        {
            server.BeginAcceptSS(ConnectionRequested, null);
            new ClientConnection(ss);
        }

    }

    /// <summary>
    /// A Connection object receives and sends information on a SS.
    /// </summary>
    public class ClientConnection
    {
        /// <summary>
        /// This is the string socket corresponding to this Connection.
        /// </summary>
        private SS socket;

        /// <summary>
        /// The first line from the socket or null if not read yet
        /// </summary>
        private string firstLine;

        /// <summary>
        /// The value of the content-length header or zero in no such header seen yet
        /// </summary>
        private int contentLength;

        // Regex pattern for making a user
        private static readonly Regex makeUserPattern = new Regex(@"^POST /BoggleService.svc/users HTTP");

        // Regex pattern for joining a game
        private static readonly Regex joinGamePattern = new Regex(@"^POST /BoggleService.svc/games HTTP");

        // Regex pattern for game update without brief parameter
        private static readonly Regex updateNoBriefPattern = new Regex(@"^GET /BoggleService.svc/games/(\d+) HTTP");

        // Regex pattern for game update with brief parameter
        private static readonly Regex updateBriefPattern = new Regex(@"^GET /BoggleService.svc/games/(\d+)(\?[bB]rief=[a-zA-Z]+) HTTP");

        //Regex pattern for game play 
        private static readonly Regex playWordPattern = new Regex(@"^PUT /BoggleService.svc/games/(\d+) HTTP");

        // Regex pattern for cancelling a game
        private static readonly Regex cancelPattern = new Regex(@"^PUT /BoggleService.svc/games HTTP");

        // Regex pattern for contentLength string
        private static readonly Regex contentLengthPattern = new Regex(@"^content-length: (\d+)", RegexOptions.IgnoreCase);

        /// <summary>
        /// Creates a new Connection object with the given socket.
        /// </summary>
        /// <param name="socket"></param>
        public ClientConnection(SS s)
        {
            socket = s;
            contentLength = 0;
            socket.BeginReceive(RequestReceived, null);

        }

        /// <summary>
        /// The callback method for socket's beginReceive method. A state machine that takes care of incoming strings.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="payload"></param>
        private void RequestReceived(String line, object payload)
        {
            if (line.Trim().Length == 0 && contentLength > 0)
            {
                socket.BeginReceive(ProcessRequest, null, contentLength);

            }
            else if (line.Trim().Length == 0)
            {
                ProcessRequest(null);
            }
            else if (firstLine != null)
            {
                Match m = contentLengthPattern.Match(line);
                if (m.Success)
                {
                    contentLength = int.Parse(m.Groups[1].ToString());
                }
                socket.BeginReceive(RequestReceived, null);
            }
            else
            {
                firstLine = line;
                socket.BeginReceive(RequestReceived, null);
            }
        }

        /// <summary>
        /// Handles the request after all relevant info has been parsed out
        /// </summary>
        /// <param name="line"></param>
        /// <param name="p"></param>
        private void ProcessRequest(string line, object p = null)
        {
            String result = "";
            // this handles 'make user' request
            if (makeUserPattern.IsMatch(firstLine))
            {
                UserNickames name = JsonConvert.DeserializeObject<UserNickames>(line);
                dynamic user = new BoggleService().Register(name, out HttpStatusCode status);
                result = ComposeResponse(user, status);
            }
            // this handles 'join game' request
            else if (joinGamePattern.IsMatch(firstLine))
            {
                GameRequest request = JsonConvert.DeserializeObject<GameRequest>(line);
                JoinResponse response = new BoggleService().Join(request, out HttpStatusCode status);
                result = ComposeResponse(response, status);

            }
            // this handles 'update game status' with brief parameter on or off
            else if (updateNoBriefPattern.IsMatch(firstLine) || updateBriefPattern.IsMatch(firstLine))
            {
                Match m;
                string gameID = "";
                string briefParam = null;
                string brief = "";

                // if brief parameter is not provided
                if (updateNoBriefPattern.Match(firstLine).Success)
                {
                    m = updateNoBriefPattern.Match(firstLine);
                    gameID = m.Groups[1].ToString();
                }

                // or not provided, parse brief parameter
                if (updateBriefPattern.Match(firstLine).Success)
                {
                    m = updateBriefPattern.Match(firstLine);

                    // game ID is embedded in first group of regex pattern
                    gameID = m.Groups[1].ToString();

                    // brief parameter is embedded in second group of regex pattern
                    briefParam = m.Groups[2].ToString();

                    // remove irrelevent strings
                    brief = briefParam.Substring(7);
                }

                GameStatus response;
                HttpStatusCode status;
                if (briefParam == null)
                {
                    response = new BoggleService().Update(gameID, "No", out status);
                }
                else
                {
                    response = new BoggleService().Update(gameID, brief, out status);
                }

                result = ComposeResponse(response, status);

            }
            // this handles playword request
            else if (playWordPattern.IsMatch(firstLine))
            {
                string gameID = playWordPattern.Match(firstLine).Groups[1].ToString();
                PlayRequest request = JsonConvert.DeserializeObject<PlayRequest>(line);
                PlayResponse response = new BoggleService().PlayWord(request, gameID, out HttpStatusCode status);
                result = ComposeResponse(response, status);

            }
            // this handles cancel game request
            else if (cancelPattern.IsMatch(firstLine))
            {

                UserObject user = JsonConvert.DeserializeObject<UserObject>(line);
                new BoggleService().CancelJoinRequest(user, out HttpStatusCode status);
                result = ComposeResponse(null, status);
            }
            // capturing whatever string requests that does not match any of the above regex patterns
            else
            {
                result = "HTTP/1.1 " + "403" + " Forbidden" + "\r\n\r\n";
            }
            socket.BeginSend(result, (x, y) => { socket.Shutdown(SocketShutdown.Both); }, null);
        }

        /// <summary>
        /// This private helper compose necessary respond string sending back to clients' requests
        /// </summary>
        /// <param name="response"></param>
        /// <param name="status"></param>
        /// <returns></returns>
        private String ComposeResponse(dynamic response, HttpStatusCode status)
        {
            String result = "HTTP/1.1 " + (int)status + " " + status + "\r\n";
            string res;

            if ((int)status / 100 == 2)
            {
                // Cancel game function does not return a response object
                if (response == null)
                    res = "";
                else
                    res = JsonConvert.SerializeObject(response);

                result += "Content-Length: " + Encoding.UTF8.GetByteCount(res) + "\r\n\r\n";
                result += res;
            }
            else
            {
                result += "\r\n";
            }

            return result;
        }
    }
}
