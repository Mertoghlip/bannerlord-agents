using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace BannerlordAgents
{
    /// <summary>
    /// The lieutenant's window (0.10.1; Mert, Oct 23: "have a button work as a shortcut (say 'enter') to go into the liutenant
    /// pop-up automatically and chat with it through that pop-up rather than through the game's own log ... its easier to approve,
    /// disapprove, or comment on its proposals as well (we could have buttons for each)"). On the campaign map, Enter opens it
    /// instead of the game's chat box, which keeps T (ChatHook, since 0.10.2). It holds the conversation (everything since the
    /// load, newest at the bottom; since 0.10.3 it scrolls, Mert: "I should be able to scroll up and down to see the full answers
    /// of my liutenant and the chat's history"), the lieutenant's proposal waiting with Approve and Decline (its list scrolls too),
    /// a line to type in (Enter sends), and Close (Esc). The
    /// game stands still while it is open, and the clock cannot be started from the map meanwhile; it runs again as it ran when the
    /// window closes. Its look is the game's own text pop-up's (LieutenantWindow.xml). Game's thread (the UI's), every frame.
    /// </summary>
    internal static class LieutenantWindow
    {
        private static GauntletLayer _layer;
        private static GauntletMovieIdentifier _movie;
        private static ScreenBase _screen;
        private static LieutenantVM _vm;
        private static CampaignTimeControlMode? _clockBefore;
        private static bool _openAsked, _closeAsked, _paused;
        // the conversation's scrollbar (found once the window is up) and the frames left to hold it at the newest line
        private static ScrollbarWidget _scrollbar;
        private static int _seek, _toEnd;
        // a new line's height is known a frame or two after it is added, so the view is held at the end a few frames
        private const int SettleFrames = 8;

        // the conversation as it is shown, kept while the window is closed (this load only)
        private static readonly List<KeyValuePair<string, uint>> Lines = new List<KeyValuePair<string, uint>>();
        private const int MaxLines = 200;

        public static bool IsOpen => _layer != null;

        /// <summary>Enter was pressed on the campaign map (from the game's chat box's look at its keys, mid-frame): the window opens next frame.</summary>
        public static void AskOpen() => _openAsked = true;

        /// <summary>Close was clicked (inside the window's own event): it closes next frame, outside the window's handling.</summary>
        public static void AskClose() => _closeAsked = true;

        public static void Reset()
        {
            if (IsOpen) Close();
            Lines.Clear();
            _openAsked = false;
        }

        /// <summary>
        /// A line of the conversation: into the window (open or not); when it is closed, into the game's message log too. With the
        /// window open, the view follows to the new line when it was at the end or the line is the player's (mine); a player
        /// scrolled back to read stays where they are.
        /// </summary>
        public static void Add(string text, uint argb, bool alsoLog, bool mine = false)
        {
            Lines.Add(new KeyValuePair<string, uint>(text, argb));
            if (Lines.Count > MaxLines) Lines.RemoveRange(0, Lines.Count - MaxLines);
            if (IsOpen)
            {
                if (mine || AtEnd()) _toEnd = SettleFrames;
                _vm.Append(text, argb, MaxLines);
            }
            else if (alsoLog)
            {
                try { InformationManager.DisplayMessage(new InformationMessage(text, Color.FromUint(argb))); }
                catch (Exception e) { Diag.Swallowed("a lieutenant's line in the message log", e); }
            }
        }

        /// <summary>The window's view of the lieutenant's waiting proposal and whether it is thinking, after any change.</summary>
        public static void Refresh()
        {
            if (IsOpen) _vm.Refresh();
        }

        /// <summary>Every frame (SubModule.OnApplicationTick): opens on request, closes when the map is gone, and Enter and Esc.</summary>
        public static void Frame()
        {
            try
            {
                if (_openAsked)
                {
                    _openAsked = false;
                    if (!IsOpen && OnMap() && Lieutenant.Active) Open();
                }
                if (!IsOpen) { _closeAsked = false; return; }
                // every frame while open: nothing that allocates
                if (_closeAsked || !OnMap() || ScreenManager.TopScreen != _screen) { _closeAsked = false; Close(); return; }
                if (_scrollbar == null && _seek > 0) { _seek--; _scrollbar = FindScrollbar(); }
                if (_toEnd > 0) { _toEnd--; if (_scrollbar != null) _scrollbar.ValueFloat = _scrollbar.MaxValue; }
                if (_layer.Input.IsHotKeyReleased("Exit")) Close();
                else if (_layer.Input.IsHotKeyReleased("Confirm")) _vm.ExecuteSend();
            }
            catch (Exception e) { Diag.Error("the lieutenant's window", e); }
        }

        private static bool OnMap() => Campaign.Current != null && TaleWorlds.MountAndBlade.Mission.Current == null && ScreenManager.TopScreen?.GetType().Name == "MapScreen";

        /// <summary>The conversation's view is at its newest line (or all of it fits, the scrollbar hidden).</summary>
        private static bool AtEnd() => _scrollbar == null || !_scrollbar.IsVisible || _scrollbar.ValueFloat >= _scrollbar.MaxValue - 4f;

        private static ScrollbarWidget FindScrollbar()
        {
            try { return _layer.UIContext.Root.FindChild("LinesScrollbar", true) as ScrollbarWidget; }
            catch (Exception e) { Diag.Swallowed("the lieutenant's window's scrollbar", e); _seek = 0; return null; }
        }

        private static void Open()
        {
            _vm = new LieutenantVM();
            _vm.Show(Lines);
            _screen = ScreenManager.TopScreen;
            _layer = new GauntletLayer("BannerlordAgentsLieutenant", 4000);
            _movie = _layer.LoadMovie("LieutenantWindow", _vm);
            _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
            _layer.InputRestrictions.SetInputRestrictions();
            _layer.IsFocusLayer = true;
            _screen.AddLayer(_layer);
            ScreenManager.TrySetFocus(_layer);
            // opens at the newest line
            _seek = 10;
            _scrollbar = FindScrollbar();
            _toEnd = SettleFrames;
            // the game stands still while the window is open (Mert: popups pause the game until closed; lieutenant_pause)
            _clockBefore = null;
            if (Settings.LieutenantPause)
            {
                Campaign c = Campaign.Current;
                _clockBefore = c.TimeControlMode != CampaignTimeControlMode.Stop ? c.TimeControlMode : (CampaignTimeControlMode?)null;
                c.TimeControlMode = CampaignTimeControlMode.Stop;
                c.SetTimeControlModeLock(true);
                _paused = true;
            }
            Diag.Count("lieutenant:window opened");
        }

        public static void Close()
        {
            try
            {
                _layer.InputRestrictions.ResetInputRestrictions();
                _layer.IsFocusLayer = false;
                ScreenManager.TryLoseFocus(_layer);
                _screen?.RemoveLayer(_layer);
                _layer.ReleaseMovie(_movie);
            }
            catch (Exception e) { Diag.Swallowed("closing the lieutenant's window", e); }
            _layer = null;
            _movie = null;
            _screen = null;
            _scrollbar = null;
            _seek = _toEnd = 0;
            _vm?.OnFinalize();
            _vm = null;
            try
            {
                Campaign c = Campaign.Current;
                if (c != null && _paused)
                {
                    c.SetTimeControlModeLock(false);
                    if (_clockBefore != null && c.TimeControlMode == CampaignTimeControlMode.Stop) c.TimeControlMode = _clockBefore.Value;
                }
            }
            catch (Exception e) { Diag.Swallowed("starting the clock after the lieutenant's window", e); }
            _clockBefore = null;
            _paused = false;
        }
    }

    /// <summary>The window's data (LieutenantWindow.xml binds to these).</summary>
    internal sealed class LieutenantVM : ViewModel
    {
        private MBBindingList<LieutenantLineVM> _lines = new MBBindingList<LieutenantLineVM>();
        private string _inputText = "", _proposalText = "", _thinkingText = "";
        private bool _hasProposal, _isThinking;

        [DataSourceProperty] public string TitleText => "Your lieutenant";
        [DataSourceProperty] public string SendText => "Send";
        [DataSourceProperty] public string CloseText => "Close";
        [DataSourceProperty] public string ApproveText => "Approve";
        [DataSourceProperty] public string DeclineText => "Decline";
        [DataSourceProperty] public string HintText => "Type to your lieutenant and press Enter. Scroll for earlier lines. Esc closes. The game is paused while this is open.";

        [DataSourceProperty]
        public MBBindingList<LieutenantLineVM> Lines
        {
            get => _lines;
            set { if (value != _lines) { _lines = value; OnPropertyChangedWithValue(value, "Lines"); } }
        }

        [DataSourceProperty]
        public string InputText
        {
            get => _inputText;
            set { if (value != _inputText) { _inputText = value ?? ""; OnPropertyChangedWithValue(_inputText, "InputText"); } }
        }

        [DataSourceProperty]
        public bool HasProposal
        {
            get => _hasProposal;
            set { if (value != _hasProposal) { _hasProposal = value; OnPropertyChangedWithValue(value, "HasProposal"); } }
        }

        [DataSourceProperty]
        public string ProposalText
        {
            get => _proposalText;
            set { if (value != _proposalText) { _proposalText = value ?? ""; OnPropertyChangedWithValue(_proposalText, "ProposalText"); } }
        }

        [DataSourceProperty]
        public bool IsThinking
        {
            get => _isThinking;
            set { if (value != _isThinking) { _isThinking = value; OnPropertyChangedWithValue(value, "IsThinking"); } }
        }

        [DataSourceProperty]
        public string ThinkingText
        {
            get => _thinkingText;
            set { if (value != _thinkingText) { _thinkingText = value ?? ""; OnPropertyChangedWithValue(_thinkingText, "ThinkingText"); } }
        }

        /// <summary>The whole conversation kept, when the window opens (newest at the bottom; the view scrolls).</summary>
        public void Show(List<KeyValuePair<string, uint>> lines)
        {
            var list = new MBBindingList<LieutenantLineVM>();
            foreach (KeyValuePair<string, uint> l in lines) list.Add(new LieutenantLineVM(l.Key, Color.FromUint(l.Value)));
            Lines = list;
            Refresh();
        }

        /// <summary>One new line while the window is open, the oldest let go past max.</summary>
        public void Append(string text, uint argb, int max)
        {
            _lines.Add(new LieutenantLineVM(text, Color.FromUint(argb)));
            while (_lines.Count > max) _lines.RemoveAt(0);
        }

        public void Refresh()
        {
            ProposalText = Lieutenant.ProposalWords();
            HasProposal = ProposalText.Length > 0;
            IsThinking = Lieutenant.Thinking;
            ThinkingText = IsThinking ? "Your lieutenant is thinking..." : "";
        }

        public void ExecuteSend()
        {
            string text = (InputText ?? "").Trim();
            if (text.Length == 0) return;
            InputText = "";
            Lieutenant.OnPlayerSays(text);
        }

        public void ExecuteApprove() => Lieutenant.Approve();
        public void ExecuteDecline() => Lieutenant.Decline();
        public void ExecuteClose() => LieutenantWindow.AskClose();
    }

    internal sealed class LieutenantLineVM : ViewModel
    {
        private readonly string _text;
        private readonly Color _color;

        public LieutenantLineVM(string text, Color color)
        {
            _text = text;
            _color = color;
        }

        [DataSourceProperty] public string Text => _text;
        [DataSourceProperty] public Color Color => _color;
    }
}
