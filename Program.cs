using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpParallelExceptionEventBugExample
{
    internal class CommandEventArgs
    {
        public Server Server { get; }
        public Channel Channel { get; }

        public CommandEventArgs(Server s, Channel c)
        {
            Server = s;
            Channel = c;
        }
    }

    internal class Channel
    {
        public async Task SendMessage(string s)
        {
            Console.WriteLine(s);
        }
    }

    internal class Client
    {
        //public Action<object, MessageEventArgs> OnMessageReceived { get; set; }
        public static event EventHandler<MessageEventArgs> OnMessageRecieved;
    }

    internal class Message
    {
        public String Text { get; }

        public Message(string s)
        {
            Text = s;
        }
    }

    internal class MessageEventArgs
    {
        public Message Message { get; }
        public User User { get; }
    }

    internal class Server
    {
    }

    internal class TriviaQuestion
    {
        public bool IsAnswerCorrect(string s)
        {
            return true;
        }
    }

    internal class TriviaQuestionPool
    {
        public static TriviaQuestionPool Instance
        {
            get { return new TriviaQuestionPool(); }
        }

        public TriviaQuestion GetRandomQuestion(HashSet<TriviaQuestion> oldQuestions)
        {
            return new TriviaQuestion();
        }
    }

    internal class User
    {
    }

    internal class Example
    {
        private readonly SemaphoreSlim _guessLock = new SemaphoreSlim(1, 1);

        private Server Server { get; }
        private Channel Channel { get; }

        private int QuestionDurationMilliseconds { get; } = 30000;
        private int HintTimeoutMilliseconds { get; } = 6000;
        public bool ShowHints { get; set; }
        private CancellationTokenSource TriviaCancelSource { get; set; }

        private TriviaQuestion CurrentQuestion { get; set; }
        public HashSet<TriviaQuestion> OldQuestions { get; } = new HashSet<TriviaQuestion>();

        public ConcurrentDictionary<User, int> Users { get; } = new ConcurrentDictionary<User, int>();

        public bool GameActive { get; private set; } = false;
        public bool ShouldStopGame { get; private set; }

        public int WinRequirement { get; }

        public Example(CommandEventArgs e, bool showHints = false, int winReq = 10)
        {
            ShowHints = showHints;
            Server = e.Server;
            Channel = e.Channel;
            WinRequirement = winReq;
            Task.Run(StartGame);
        }

        private async Task StartGame()
        {
            while (!ShouldStopGame)
            {
                //reset the cancellation source
                TriviaCancelSource = new CancellationTokenSource();
                var token = TriviaCancelSource.Token;
                //load question
                CurrentQuestion = TriviaQuestionPool.Instance.GetRandomQuestion(OldQuestions);
                if (CurrentQuestion == null)
                {
                    await Channel.SendMessage("null").ConfigureAwait(false);
                    await End().ConfigureAwait(false);
                    return;
                }
                //add current question to the exclusion list
                OldQuestions.Add(CurrentQuestion);
                await Channel.SendMessage(CurrentQuestion.ToString());
                //add PotentialGuess to OnMessageReceived
                Client.OnMessageRecieved += PotentialGuess;
                //allow people to guess
                GameActive = true;

                try
                {
                    //hint
                    await Task.Delay(HintTimeoutMilliseconds, token).ConfigureAwait(false);
                    if (ShowHints)
                    {
                        await Channel.SendMessage("hint");
                    }
                    await
                        Task.Delay(QuestionDurationMilliseconds - HintTimeoutMilliseconds, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException) { }
                GameActive = false;
                if (!TriviaCancelSource.IsCancellationRequested)
                {
                    await Channel.SendMessage("correct answer was : asdf").ConfigureAwait(false);
                }
            }
        }

        private async Task End()
        {
            ShouldStopGame = true;
            await Channel.SendMessage("game ended").ConfigureAwait(false);
        }

        public async Task StopGame()
        {
            if (!ShouldStopGame)
            {
                await Channel.SendMessage("will stop after next question");
            }
            ShouldStopGame = true;
        }

        private async void PotentialGuess(object sender, MessageEventArgs e)
        {
            try
            {
                var guess = false;
                await _guessLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (GameActive && CurrentQuestion.IsAnswerCorrect(e.Message.Text) &&
                        !TriviaCancelSource.IsCancellationRequested)
                    {
                        Users.TryAdd(e.User, 0);
                        Users[e.User]++;
                        guess = true;
                    }
                }
                finally
                {
                    _guessLock.Release();
                }

                if (!guess) return;
                TriviaCancelSource.Cancel();
                await Channel.SendMessage("correct answer guessed").ConfigureAwait(false);
                if (Users[e.User] != WinRequirement) return;
                ShouldStopGame = true;
                await Channel.SendMessage("winner winner chicken dinner");
            } catch { }
        }
    }

}


