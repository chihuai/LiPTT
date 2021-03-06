﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel.Core;

namespace LiPTT
{
    public class PttPageViewModel : INotifyPropertyChanged
    {
        private string state;

        public PttPageViewModel()
        {
            LiPTT.PttEventEchoed += LiPTT_PttEventEchoed;
            CoreApplication.Resuming += (a, b) =>
            {
                LiPTT.PttEventEchoed += LiPTT_PttEventEchoed;
                State = "重新連線中...";
                OnPropertyChanged("State");
            };
        }

        private void LiPTT_PttEventEchoed(PTTClient sender, LiPttEventArgs e)
        {
            switch (e.State)
            {
                case PttState.Disconnected:
                    if (!LiPTT.Client.PTTWrongResponse) State = "未連線";
                    break;
                case PttState.Connecting:
                    State = "連線中...";
                    break;
                case PttState.ConnectFailedTCP:
                    State = "TCP 連線失敗";
                    break;
                case PttState.ConnectFailedWebSocket:
                    State = "WebSocket 連線失敗";
                    break;
                case PttState.Board:
                    State = "看板";
                    break;
                case PttState.SearchBoard:
                    State = "搜尋看板";
                    break;
                case PttState.Disconnecting:
                    if (!LiPTT.Client.PTTWrongResponse) State = "斷線中...";
                    break;
                case PttState.Login:
                    State = "(請輸入帳號)";
                    break;
                case PttState.Password:
                    State = "(請輸入密碼)";
                    break;
                case PttState.Loginning:
                    State = "登入中...";
                    break;
                case PttState.Synchronizing:
                    State = "更新與同步個人資訊中...";
                    break;
                case PttState.Accept:
                    State = "密碼正確";
                    break;
                case PttState.AlreadyLogin:
                    State = "有重複登入，踢掉中...";
                    break;
                case PttState.OverLoading:
                    State = "PTT被你玩壞惹?";
                    break;
                case PttState.LoginSoMany:
                    State = "登入太頻繁 請稍後在試";
                    break;
                case PttState.Kicked:
                    State = "誰踢我";
                    break;
                case PttState.WrongPassword:
                    State = "密碼不對或無此帳號";
                    break;
                case PttState.Angel:
                    State = "小天使?";
                    break;
                case PttState.WrongLog:
                    State = "要刪除登入錯誤資訊嗎?";
                    break;
                case PttState.MainPage:
                    State = "主功能表";
                    break;
                case PttState.PressAny:
                    State = "(請按任意鍵繼續...)";
                    break;
                default:
                    State = "未定義狀態";
                    break;
            }
        }

        public string State
        {
            get
            {
                return state;
            }
            set
            {
                state = value;
                OnPropertyChanged("State");
                
            }
        }

        private void OnPropertyChanged([CallerMemberName] String propertyName = "")
        {
            var action = LiPTT.RunInUIThread(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
