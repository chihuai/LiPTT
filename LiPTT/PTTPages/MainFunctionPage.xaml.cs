﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Threading;
using System.ComponentModel;
using System.Runtime.CompilerServices;

// 空白頁項目範本已記錄在 https://go.microsoft.com/fwlink/?LinkId=234238

namespace LiPTT
{
    public sealed partial class MainFunctionPage : Page, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName]string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainFunctionPage()
        {
            this.DataContext = this;

            this.InitializeComponent();

            BoardGridView.Items.Add(new MyKeyValuePair("Windows", "Windows"));
            BoardGridView.Items.Add(new MyKeyValuePair("笨蛋", "StupidClown"));
            BoardGridView.Items.Add(new MyKeyValuePair("Soft Job", "Soft_Job"));
            BoardGridView.Items.Add(new MyKeyValuePair("八卦", "Gossiping"));
            BoardGridView.Items.Add(new MyKeyValuePair("LOL", "LoL"));
            BoardGridView.Items.Add(new MyKeyValuePair("表特", "Beauty"));
            BoardGridView.Items.Add(new MyKeyValuePair("電蝦", "PC_Shopping"));
            BoardGridView.Items.Add(new MyKeyValuePair("少女前線", "GirlsFront"));
            BoardGridView.Items.Add(new MyKeyValuePair("C# 程式設計", "C_Sharp"));

            ControlVisible = Visibility.Visible;
        }

        List<string> RelatedTable = new List<string>();

        private bool searching = false;

        private bool control_visible;

        public Visibility ControlVisible
        {
            get
            {
                if (control_visible)
                    return Visibility.Visible;
                else
                    return Visibility.Collapsed;
            }
            set
            {
                if ((control_visible && value == Visibility.Collapsed) || (!control_visible && value == Visibility.Visible))
                {
                    if (value == Visibility.Visible)
                        control_visible = true;
                    else
                        control_visible = false;
                    NotifyPropertyChanged("ControlVisible");
                    NotifyPropertyChanged("RingActive");
                }
            }
        }

        public bool RingActive
        {
            get
            {
                return !control_visible;
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            ItemClick = false;
            ControlVisible = Visibility.Visible;
        }

        private void BoardAutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                if (searching) return;

                if (BoardAutoSuggestBox.Text.Length == 0)
                {
                    RelatedTable.Clear();
                    BoardAutoSuggestBox.ItemsSource = null;
                    return;
                }

                if (!AcceptString(BoardAutoSuggestBox.Text)) return;

                searching = true;

                RelatedTable.Clear();
                BoardAutoSuggestBox.ItemsSource = null;

                LiPTT.PttEventEchoed += SearchBoard;
                LiPTT.SendMessage('s', BoardAutoSuggestBox.Text, 0x20);
            }
        }

        private void SearchBoard(PTTClient sender, LiPttEventArgs e)
        {
            Regex regex = new Regex(@"([\w-_]+)");
            Match match;
            ScreenBuffer screen = sender.Screen;

            switch (e.State)
            {
                case PttState.SearchBoard:
                    LiPTT.PttEventEchoed -= SearchBoard;
                    var msg = screen.ToString(1, 34, 20).Trim();

                    match = regex.Match(msg);

                    if (match.Success)
                    {
                        string suggestion = msg.Substring(match.Index, match.Length);

                        var action = LiPTT.RunInUIThread(() =>
                        {
                            if (BoardAutoSuggestBox.Text.Length <= suggestion.Length) RelatedTable.Add(suggestion);
                        });

                         
                    }

                    ClearSearch();

                    var a = LiPTT.RunInUIThread(() =>
                    {
                        BoardAutoSuggestBox.ItemsSource = RelatedTable;
                    });

                    break;
                case PttState.RelatedBoard:

                    for (int i = 3; i < 23; i++)
                    {
                        string k = screen.ToString(i).Replace('\0', ' ');

                        match = regex.Match(k, 0);
                        if (match.Success) RelatedTable.Add(k.Substring(match.Index, match.Length));

                        match = regex.Match(k, 22);
                        if (match.Success) RelatedTable.Add(k.Substring(match.Index, match.Length));

                        match = regex.Match(k, 44);
                        if (match.Success) RelatedTable.Add(k.Substring(match.Index, match.Length));

                    }

                    if (new Regex("按空白鍵可列出更多項目").Match(screen.ToString(23)).Success)
                    {
                        LiPTT.PressSpace();
                    }
                    else
                    {
                        LiPTT.PttEventEchoed -= SearchBoard;

                        RelatedTable.Sort();    
                        ClearSearch();

                        var t = LiPTT.RunInUIThread(() => 
                        {
                            BoardAutoSuggestBox.ItemsSource = RelatedTable;
                        }); 
                    }
                    break;
            }
        }
       
        private bool AcceptString(string s)
        {
            foreach (char c in s)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c == '-' || c <= '_')))
                {
                    return false;
                }
            }

            return true;
        }

        private void ClearSearch()
        {
            string lastText = "";
            var msg = LiPTT.Screen.ToString(1, 34, 20).Trim();
            Regex regex = new Regex(@"([\w-_]+)");
            Match match = regex.Match(msg);
            if (match.Success)
            {
                lastText = msg.Substring(match.Index, match.Length);
            }
            else return;

            byte[] back = new byte[lastText.Length + 1];
            back[lastText.Length] = 0x0D;
            for (int i = 0; i < lastText.Length; i++) back[i] = 0x08;
            LiPTT.SendMessage(back);

            searching = false;
        }

        private void BoardAutoSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.QueryText == "") return;

            if (args.ChosenSuggestion is string chosen)
            {
                LiPTT.PttEventEchoed += GoToBoard;
                LiPTT.SendMessage('s', chosen, 0x0D);
            }
            else if (RelatedTable.Count > 0 && RelatedTable.First() == args.QueryText)
            {
                LiPTT.PttEventEchoed += GoToBoard;
                LiPTT.SendMessage('s', args.QueryText, 0x0D);
            }
            else if (RelatedTable.Count > 0)
            {
                BoardAutoSuggestBox.Text = RelatedTable.First();
            }
        }

        private bool pressAny = false;

        private void GoToBoard(PTTClient sender, LiPttEventArgs e)
        {
            switch (e.State)
            {
                case PttState.PressAny:
                    if (!pressAny)
                    {
                        pressAny = true;
                        LiPTT.PressAnyKey();
                    }
                    
                    break;
                case PttState.Board:
                    LiPTT.PageEnd();
                    LiPTT.PttEventEchoed -= GoToBoard;
                    var t = LiPTT.RunInUIThread(() => {
                        LiPTT.Frame.Navigate(typeof(BoardPage));
                    });
                    break;
            }
        }

        private bool ItemClick = false;

        private void GridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (ItemClick) return;

            ItemClick = true;
            ControlVisible = Visibility.Collapsed;

            if (e.ClickedItem is MyKeyValuePair kv)
            {
                LiPTT.PttEventEchoed += GoToBoard;
                LiPTT.SendMessage('s', kv.Value, 0x0D);
            }
        }
    }

    public class MyKeyValuePair : INotifyPropertyChanged
    {
        public object Key
        {
            get { return k; }
            set
            {
                k = value;
                NotifyPropertyChanged("Key");
            }
        }
        public object Value
        {
            get { return v; }
            set
            {
                v = value;
                NotifyPropertyChanged("Value");
            }
        }

        private object k;
        private object v;

        public MyKeyValuePair(object k, object v)
        {
            this.k = k;
            this.v = v;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName]string propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
