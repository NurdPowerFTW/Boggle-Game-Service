// Created by Wei-Tung Tang and Chen-Chia Wang

using Boggle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Web;


namespace Boggle
{
    /// <summary>
    /// Used by register call.
    /// </summary>
    public class UserNickames
    {
        public string Nickname { get; set; }
    }

    /// <summary>
    /// Register call response to client.
    /// </summary>
    [DataContract]
    public class UserObject
    {
        [DataMember]
        public string UserToken { get; set; }

    }

    /// <summary>
    /// Receive a game call request.
    /// </summary>
    public class GameRequest
    {
        [DataMember]
        public string UserToken { get; set; }
        [DataMember(EmitDefaultValue = false)]
        public int? TimeLimit { get; set; }
        [DataMember(EmitDefaultValue = false)]
        public string Word { get; set; }
    }

    /// <summary>
    /// Join game successful respond.
    /// </summary>
    [DataContract]
    public class JoinResponse
    {
        [DataMember]
        public string GameID { get; set; }
    }

    [DataContract]
    public class UpdateRequest
    {
        [DataMember]
        public string GameID { get; set; }
        [DataMember(EmitDefaultValue = false)]
        public Dictionary<string, string> UrlParameters { get; set; }
    }

    /// <summary>
    /// Receives request from client for play word function.
    /// </summary>
    [DataContract]
    public class PlayRequest
    {
        [DataMember]
        public string UserToken { get; set; }

        // Question mark modifier allows nullable input when there is no input for this property.
        // This is useful for game request that does not specify a value for some memebers
        [DataMember(EmitDefaultValue = false)]
        public int? TimeLimit { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string Word { get; set; }
    }

    /// <summary>
    /// Response of a play word request.
    /// </summary>
    [DataContract]
    public class PlayResponse
    {
        [DataMember]
        public int Score { get; set; }
    }

    /// <summary>
    /// A player object that can be used to report game status when Update api is called by client
    /// </summary>
    [DataContract]
    public class Player
    {
        [DataMember]
        public int Score { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string Nickname { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<WordsPlayed> WordsPlayed { get; set; }

        public string UserToken { get; set; }
    }

    /// <summary>
    /// Used to track players' word inputs and scores throughout the game.
    /// </summary>
    public class WordsPlayed
    {
        public string Word { get; set; }
        public int Score { get; set; }

        public override bool Equals(object obj)
        {
            if (!(obj is WordsPlayed) || Word == null)
                return false;

            WordsPlayed other = (WordsPlayed)obj;
            return Word.Equals(other.Word);
        }

        public override int GetHashCode()
        {
            if (Word == null)
                return 0;
            return Word.GetHashCode();
        }
    }

    /// <summary>
    /// Used to present an overal status for each game. Used locally on the server.
    /// </summary>
    public class GameInfo
    {
        public BoggleBoard BoardData { get; set; }

        public DateTime StartTime { get; set; }

        public TimeSpan TimeLimit { get; set; }

        public Player Player1 { get; set; }

        public Player Player2 { get; set; }
    }

    /// <summary>
    /// Used to provide reponse for update api called by client.
    /// </summary>
    [DataContract]
    public class GameStatus
    {
        [DataMember]
        public string GameState { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string Board { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public int? TimeLimit { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public int? TimeLeft { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public Player Player1 { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public Player Player2 { get; set; }
    }

    
}