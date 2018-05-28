// Created by Wei-Tung Tang and Chen-Chia Wang

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using static System.Net.HttpStatusCode;

namespace Boggle
{
    /// <summary>
    /// This class provides implementation for Boggle Server.
    /// </summary>
    public class BoggleService
    {
        /// <summary>
        /// All legal words
        /// </summary>
        private static readonly ISet<string> dictionary;

        /// <summary>
        /// A reference to database
        /// </summary>
        private static readonly string BoggleDB;

        /// <summary>
        /// A static constructor to initalize database and dictionary with valid words
        /// </summary>
        static BoggleService()
        {
            BoggleDB = @"Data Source = (LocalDB)\MSSQLLocalDB; AttachDbFilename = |DataDirectory|\BoggleDB.mdf; Integrated Security = True";
            dictionary = new HashSet<string>();
            using (StreamReader words = new StreamReader(AppDomain.CurrentDomain.BaseDirectory + @"/dictionary.txt"))
            {
                string word;
                while ((word = words.ReadLine()) != null)
                {
                    dictionary.Add(word.ToUpper());
                }
            }
            // BoggleDB = ConfigurationManager.ConnectionStrings["BoggleDB"].ConnectionString;
        }

        /// <summary>
        /// The most recent call to SetStatus determines the response code used when
        /// an http response is sent.
        /// </summary>
        /// <param name="status"></param>
        private static void SetStatus(HttpStatusCode status)
        {
            //WebOperationContext.Current.OutgoingResponse.StatusCode = status;
        }


        /// <summary>
        /// get most recent game id from the database
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="trans"></param>
        /// <returns></returns>
        private int GetRecentGameID(SqlConnection conn, SqlTransaction trans)
        {
            using (SqlCommand comm = new SqlCommand("SELECT MAX(GameID) FROM Games", conn, trans))
            {
                using (SqlDataReader reader = comm.ExecuteReader())
                {
                    if (!reader.HasRows)
                    {
                        return -1;
                    }
                    reader.Read();
                    return reader.GetInt32(0);
                }
            }
        }

        /// <summary>
        /// Registers a new user.
        /// If user.Name is null or is empty after trimming, responds with status code Forbidden.
        /// Otherwise, creates a user, returns the user's token, and responds with status code Created. 
        /// </summary>
        /// <param name="users"></param>
        /// <returns></returns>
        public UserObject Register(UserNickames users, out HttpStatusCode status)
        {
            string usertoken;

            if (users.Nickname == null || users.Nickname.Trim().Length > 50 || users.Nickname.Trim().Length == 0)
            {
                status = Forbidden;
                return null;
            }

            using (SqlConnection conn = new SqlConnection(BoggleDB))
            {
                conn.Open();
                using (SqlTransaction trans = conn.BeginTransaction())
                {
                    usertoken = GenerateUserID(conn, trans);
                    using (SqlCommand comm = new SqlCommand("INSERT INTO Users (UserID, Nickname) VALUES (@UserID, @Nickname)", conn, trans))
                    {
                        comm.Parameters.AddWithValue("@UserID", usertoken);
                        comm.Parameters.AddWithValue("@Nickname", users.Nickname);
                        comm.ExecuteNonQuery();
                    }
                    trans.Commit();
                }
            }

            status = Created;
            return new UserObject { UserToken = usertoken };
        }

        /// <summary>
        /// This private helper guarantees that each user gets an unique identifier even if there are duplicated name values
        /// exisit in BoggleDB
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="trans"></param>
        /// <returns></returns>
        private string GenerateUserID(SqlConnection conn, SqlTransaction trans)
        {
            string newID = Guid.NewGuid().ToString();

            // Make a query to make sure there is no duplicated user token
            while (true)
            {
                using (SqlCommand comm = new SqlCommand("select UserID from Users where UserID = '" + newID + "'", conn, trans))
                {
                    using (SqlDataReader reader = comm.ExecuteReader())
                    {
                        if (reader.HasRows)
                            newID = Guid.NewGuid().ToString();
                        else
                            break;
                    }
                }
            }

            return newID;
        }

        /// <summary>
        /// Put a registered user into a game. If the game already has one player in it, the game will start; otherwise
        /// it's a pending game. If usertoken is already a player in the pending game, responds with status 409 (conflict)
        /// </summary>
        /// <param name="games"></param>
        /// <returns></returns>
        public JoinResponse Join(GameRequest games, out HttpStatusCode status)
        {

            if (games.UserToken == null || games.TimeLimit > 120 || games.TimeLimit < 5)
            {
                status = Forbidden;
                return null;
            }

            using (SqlConnection conn = new SqlConnection(BoggleDB))
            {
                conn.Open();
                using (SqlTransaction trans = conn.BeginTransaction())
                {

                    // Check if usertoken is recorded in BoggleDB
                    if (!IsUserTokenValid(games.UserToken, conn, trans))
                    {
                        status = Forbidden;
                        return null;
                    }

                    // Check if player is already in a game
                    if (IsPlayerInGame(games.UserToken, conn, trans))
                    {
                        status = Conflict;
                        return null;
                    }

                    // Determine if this is going to be a new pending game or an active game
                    int playerNumber = AddPlayerToGame(games.UserToken, (int)games.TimeLimit, conn, trans);

                    int gameID = GetRecentGameID(conn, trans);
                    trans.Commit();

                    JoinResponse response = new JoinResponse { GameID = gameID.ToString() };

                    status = playerNumber == 1 ? Accepted : Created;
                    return response;

                }
            }
        }

        /// <summary>
        /// This private helper determines if user token is recorded in BoggleDB
        /// </summary>
        /// <param name="userToken"></param>
        /// <param name="conn"></param>
        /// <param name="trans"></param>
        /// <returns></returns>
        private bool IsUserTokenValid(string userToken, SqlConnection conn, SqlTransaction trans)
        {
            using (SqlCommand comm = new SqlCommand("SELECT Nickname FROM Users WHERE UserID = @UserID", conn, trans))
            {
                comm.Parameters.AddWithValue("@UserID", userToken);

                using (SqlDataReader reader = comm.ExecuteReader())
                    return reader.HasRows;
            }
        }

        /// <summary>
        /// Cancel an on-going game or a join pending game by providing a valid user token who isn't engaging in an active game.
        /// </summary>
        /// <param name="token"></param>
        public void CancelJoinRequest(UserObject token, out HttpStatusCode status)
        {
            if (token.UserToken == null)
            {
                status = Forbidden;
            }

            using (SqlConnection conn = new SqlConnection(BoggleDB))
            {
                conn.Open();
                using (SqlTransaction trans = conn.BeginTransaction())
                {
                    if (!IsPlayerInGame(token.UserToken, conn, trans))
                    {
                        status = Forbidden;
                    }
                    else
                    {
                        // Delete the game from database regardless of the game status
                        using (SqlCommand comm = new SqlCommand("DELETE Games WHERE Player2 IS NULL", conn, trans))
                        {
                            comm.ExecuteNonQuery();
                            status = OK;
                        }
                    }
                    trans.Commit();
                }
            }
        }

        /// <summary>
        /// This private helper determine if the recently joined player is 1st or 2nd player and create a new pending game 
        /// if necessary
        /// </summary>
        /// <param name="userToken"></param>
        /// <param name="timeLimit"></param>
        /// <returns></returns>
        private int AddPlayerToGame(string userToken, int timeLimit, SqlConnection conn, SqlTransaction trans)
        {
            int TimeLeftAverage = timeLimit;

            // Used to determine if a pending game exist in DB
            bool DoesPendingGameExist;

            // Probe into the Games table to see if a pending game exists
            using (SqlCommand comm = new SqlCommand("SELECT TimeLimit FROM Games WHERE Player2 IS NULL", conn, trans))
            {
                using (SqlDataReader reader = comm.ExecuteReader())
                {
                    DoesPendingGameExist = reader.HasRows;

                    reader.Read();
                    if (DoesPendingGameExist && !reader.IsDBNull(0))
                        TimeLeftAverage = (TimeLeftAverage + reader.GetInt32(0)) / 2;
                }
            }

            // If there is already a pending game, update the Games table with player2 info.
            if (DoesPendingGameExist)
            {
                using (SqlCommand comm = new SqlCommand("UPDATE Games SET Player2 = @Player2, " + "Board = @Board, " + "TimeLimit = @TimeLimit, " +
                    "StartTime = @StartTime WHERE Player2 IS NULL", conn, trans))
                {
                    comm.Parameters.AddWithValue("@Player2", userToken);
                    comm.Parameters.AddWithValue("@Board", (new BoggleBoard()).ToString());
                    comm.Parameters.AddWithValue("@TimeLimit", TimeLeftAverage);
                    comm.Parameters.AddWithValue("@StartTime", DateTime.Now.ToString("yyyy-MM-dd H:mm:ss"));

                    comm.ExecuteNonQuery();
                    return 2;
                }
            }

            // When there is no pending game, create a new pending game and insert player1 into Games table
            using (SqlCommand comm = new SqlCommand("INSERT INTO Games (Player1, TimeLimit) VALUES (@Player1, @TimeLimit)", conn, trans))
            {
                comm.Parameters.AddWithValue("@Player1", userToken);
                comm.Parameters.AddWithValue("@TimeLimit", TimeLeftAverage);

                comm.ExecuteNonQuery();
                return 1;
            }

        }

        /// <summary>
        /// Enable user to continuously contact server to update current game status when a valid game id is provided 
        /// Brief parameter allows user to get less descriptive response.
        /// </summary>
        /// <param name="GameID"></param>
        /// <param name="Brief"></param>
        /// <returns></returns>
        public GameStatus Update(string GameID, string Brief, out HttpStatusCode status)
        {

            // If gameID is obviously invalid, return with forbidden status.
            if (!Int32.TryParse(GameID, out int id) || id < 1)
            {
                status = Forbidden;
                return null;
            }

            using (SqlConnection conn = new SqlConnection(BoggleDB))
            {
                conn.Open();
                using (SqlTransaction trans = conn.BeginTransaction())
                {
                    // Make provided GameID is valid
                    using (SqlCommand comm = new SqlCommand("SELECT * FROM Games WHERE GameID = @GameID", conn, trans))
                    {
                        comm.Parameters.AddWithValue("@GameID", id);
                        using (SqlDataReader reader = comm.ExecuteReader())
                        {
                            if (!reader.HasRows)
                            {
                                status = Forbidden;
                                return null;
                            }
                        }
                    }

                    // Start composing game status info
                    GameStatus response = new GameStatus();
                    int timeLeft = ComputeTimeLeft(id, conn, trans);


                    if (timeLeft < 0)
                    {
                        status = OK;
                        return new GameStatus { GameState = "pending" };
                    }

                    // Info needed to be sent with or without brieft parameter
                    response.TimeLeft = timeLeft;
                    response.GameState = timeLeft > 0 ? "active" : "completed";

                    // Get player record
                    Player player1 = FetchPlayerInfo(true, id, conn, trans);
                    Player player2 = FetchPlayerInfo(false, id, conn, trans);

                    response.Player1 = new Player { Score = player1.Score };
                    response.Player2 = new Player { Score = player2.Score };


                    if (Brief == "yes")
                    {
                        status = OK;
                        return response;
                    }

                    // If brief is not provided, send in these info
                    response.Player1.Nickname = player1.Nickname;
                    response.Player2.Nickname = player2.Nickname;
                    response.Board = FetchBoard(id, conn, trans).ToString();

                    // TimeLimit is recorded in the DB
                    using (SqlCommand comm = new SqlCommand("SELECT TimeLimit FROM Games WHERE GameID = @GameID", conn, trans))
                    {
                        comm.Parameters.AddWithValue("@GameID", id);
                        using (SqlDataReader reader = comm.ExecuteReader())
                        {
                            reader.Read();
                            response.TimeLimit = reader.GetInt32(0);
                        }
                    }

                    // Send in words played record when game is completed
                    if (response.GameState.Equals("completed"))
                    {
                        response.Player1.WordsPlayed = player1.WordsPlayed;
                        response.Player2.WordsPlayed = player2.WordsPlayed;
                    }

                    trans.Commit();
                    status = OK;
                    return response;

                }
            }
        }

        /// <summary>
        /// This private helper fetch player record from DB.
        /// </summary>
        /// <param name="isPlayer1"></param>
        /// <param name="gameID"></param>
        /// <param name="conn"></param>
        /// <param name="trans"></param>
        /// <returns></returns>
        private Player FetchPlayerInfo(bool isPlayer1, int gameID, SqlConnection conn, SqlTransaction trans)
        {
            string playerStr = "Player" + (isPlayer1 ? "1" : "2");
            string userToken;
            string nickname;
            List<WordsPlayed> wordsPlayed;
            int score = 0;

            // Get usertoken from Games table
            using (SqlCommand comm = new SqlCommand("SELECT " + playerStr + " FROM Games WHERE GameID = @GameID", conn, trans))
            {
                comm.Parameters.AddWithValue("@GameID", gameID);

                using (SqlDataReader reader = comm.ExecuteReader())
                {
                    if (!reader.HasRows)
                        return null;

                    reader.Read();
                    userToken = reader.GetString(0);
                }
            }

            // Get player nickname from Users table
            using (SqlCommand comm = new SqlCommand("SELECT Nickname FROM Users WHERE UserID = @UserID", conn, trans))
            {
                comm.Parameters.AddWithValue("@UserID", userToken);
                using (SqlDataReader reader = comm.ExecuteReader())
                {
                    reader.Read();
                    nickname = reader.GetString(0);
                }
            }

            // Fetch words played record and score
            wordsPlayed = FetchWords(userToken, gameID, conn, trans);
            foreach (WordsPlayed entry in wordsPlayed)
            {
                score += entry.Score;
            }

            return new Player { Nickname = nickname, Score = score, WordsPlayed = wordsPlayed };
        }

        /// <summary>
        /// This private helper fetch player words played record as well as score from Words table
        /// </summary>
        /// <param name="playerToken"></param>
        /// <param name="gameID"></param>
        /// <param name="conn"></param>
        /// <param name="trans"></param>
        /// <returns></returns>
        private List<WordsPlayed> FetchWords(string playerToken, int gameID, SqlConnection conn, SqlTransaction trans)
        {
            List<WordsPlayed> result = new List<WordsPlayed>();

            using (SqlCommand comm = new SqlCommand("SELECT Word, Score FROM Words WHERE " +
                "GameID = @GameID and Player = @Player", conn, trans))
            {
                comm.Parameters.AddWithValue("@GameID", gameID);
                comm.Parameters.AddWithValue("@Player", playerToken);

                using (SqlDataReader reader = comm.ExecuteReader())
                {
                    if (!reader.HasRows)
                        return result;

                    while (reader.Read())
                    {
                        result.Add(new WordsPlayed { Word = reader.GetString(0), Score = reader.GetInt32(1) });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// This private helper fetch BoggleBoard that is recorded in Games table
        /// </summary>
        /// <param name="gameID"></param>
        /// <param name="conn"></param>
        /// <param name="trans"></param>
        /// <returns></returns>
        private BoggleBoard FetchBoard(int gameID, SqlConnection conn, SqlTransaction trans)
        {
            using (SqlCommand comm = new SqlCommand("SELECT Board FROM Games WHERE GameID = @GameID", conn, trans))
            {
                comm.Parameters.AddWithValue("@GameID", gameID);

                using (SqlDataReader reader = comm.ExecuteReader())
                {
                    if (!reader.HasRows)
                        return null;

                    reader.Read();
                    return new BoggleBoard(reader.GetString(0));
                }
            }
        }

        /// <summary>
        /// This private helper computes remaining game time by fetching startTime and timelimit from Games table
        /// </summary>
        /// <param name="GameID"></param>
        /// <param name="conn"></param>
        /// <param name="trans"></param>
        /// <returns></returns>
        private int ComputeTimeLeft(int GameID, SqlConnection conn, SqlTransaction trans)
        {
            DateTime startTime;
            TimeSpan timeLimit;

            using (SqlCommand comm = new SqlCommand("SELECT TimeLimit, StartTime FROM Games WHERE GameID = @GameID", conn, trans))
            {
                comm.Parameters.AddWithValue("@GameID", GameID);
                using (SqlDataReader reader = comm.ExecuteReader())
                {
                    // Either there is no pending game, or no game at all, return -1
                    if (!(reader.HasRows))
                    {
                        return -1;
                    }

                    reader.Read();

                    if (reader.IsDBNull(1))
                    {
                        return -1;
                    }

                    timeLimit = TimeSpan.FromSeconds(reader.GetInt32(0));
                    startTime = reader.GetDateTime(1);
                }
            }
            TimeSpan timeLeft = startTime + timeLimit - DateTime.Now;
            if (timeLeft.TotalSeconds >= 0)
            {
                return (int)timeLeft.TotalSeconds;
            }
            else
            {
                return 0;
            }

        }

        /// <summary>
        /// check if the player exists in the game
        /// </summary>
        /// <param name="PlayerToken"></param>
        /// <param name="conn"></param>
        /// <param name="trans"></param>
        /// <param name="gameID"></param>
        /// <returns></returns>
        private bool IsPlayerInGame(string userToken, SqlConnection conn, SqlTransaction trans, int gameID = -1)
        {
            // If the gameID is -1, check for the pending game.
            if (gameID < 0)
            {
                using (SqlCommand comm = new SqlCommand("SELECT Player1 FROM Games WHERE Player2 IS NULL", conn, trans))
                {
                    using (SqlDataReader reader = comm.ExecuteReader())
                    {
                        if (!reader.HasRows)
                            return false;

                        reader.Read();
                        return userToken.Equals(reader.GetString(0));
                    }
                }
            }

            // Else player might in an active game
            using (SqlCommand comm = new SqlCommand("SELECT Player1, Player2 FROM Games WHERE GameID = @GameID", conn, trans))
            {
                comm.Parameters.AddWithValue("@GameID", gameID);

                using (SqlDataReader reader = comm.ExecuteReader())
                {
                    if (!reader.HasRows)
                        return false;

                    reader.Read();

                    // Compare usertoken with player1 token and player2 token to see if player is in game
                    return userToken.Equals(reader.GetString(0)) || userToken.Equals(reader.GetString(1));
                }
            }
        }

        /// <summary>
        /// Takes a valid game ID and process Boggle game logic and returns a score based on word evaluation.
        /// </summary>
        /// <param name="games"></param>
        /// <param name="GameID"></param>
        /// <returns></returns>
        public PlayResponse PlayWord(PlayRequest games, string GamedID, out HttpStatusCode status)
        {

            string word = games.Word;
            string trimmed;

            // Various types of condition checks for request object, played word and game ID
            if (word == null || (trimmed = word.Trim()).Length == 0 || games.UserToken == null)
            {
                status = Forbidden;
                return null;
            }
            if (!Int32.TryParse(GamedID, out int id))
            {
                status = Forbidden;
                return null;
            }

            PlayResponse response = new PlayResponse();
            using (SqlConnection conn = new SqlConnection(BoggleDB))
            {
                conn.Open();
                using (SqlTransaction trans = conn.BeginTransaction())
                {
                    if (!IsUserTokenValid(games.UserToken, conn, trans) || id < 1 || id > GetRecentGameID(conn, trans) ||
                        !IsPlayerInGame(games.UserToken, conn, trans, id))
                    {
                        status = Forbidden;
                        return null;
                    }

                    if (ComputeTimeLeft(id, conn, trans) < 1)
                    {
                        status = Conflict;
                        return null;
                    }

                    // If everything is fine at this point, insert the played word into DB
                    using (SqlCommand comm = new SqlCommand("INSERT INTO Words (Word, Player, GameID, Score) VALUES (@Word, @Player, @GameID, @Score)", conn, trans))
                    {
                        BoggleBoard board = FetchBoard(id, conn, trans);
                        List<WordsPlayed> playedList = FetchWords(games.UserToken, id, conn, trans);

                        // Compute score with gameboard and played word list
                        int score = ComputeScore(trimmed, board, playedList);
                        comm.Parameters.AddWithValue("@Word", trimmed);
                        comm.Parameters.AddWithValue("@Player", games.UserToken);
                        comm.Parameters.AddWithValue("@GameID", id);
                        comm.Parameters.AddWithValue("@Score", score);
                        // Compose response message
                        response.Score = score;
                        comm.ExecuteNonQuery();
                    }

                    trans.Commit();
                }

            }
            status = OK;
            return response;
        }

        /// <summary>
        /// This private helper evaluate player's word input and returns a valid score.
        /// </summary>
        /// <param name="trimmed"></param>
        /// <param name="board"></param>
        /// <returns></returns>
        private int ComputeScore(string trimmed, BoggleBoard board, List<WordsPlayed> words)
        {

            int wordCount = trimmed.Length;
            WordsPlayed PlayedWord = new WordsPlayed();
            PlayedWord.Word = trimmed;

            // If word is less than 3 words, no point. Same goes for duplicated inputs.
            if (wordCount < 3 || words.Contains(PlayedWord))
                return 0;

            // If word is not present in the dictionary.txt
            if (!board.CanBeFormed(trimmed) || !dictionary.Contains(trimmed))
                return -1;

            // Assign score based on the length of word input
            if (wordCount == 3 || wordCount == 4)
            {
                return 1;
            }
            else if (wordCount == 5)
            {
                return 2;
            }
            else if (wordCount == 6)
            {
                return 3;
            }
            else if (wordCount == 7)
            {
                return 5;
            }

            // word inputs > 7 gets 11 points
            return 11;
        }
    }
}
