using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using SaberSurgeon.Chat;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SaberSurgeon.UI.Controllers
{
    [ViewDefinition("SaberSurgeon.UI.Views.FloatingChat.bsml")]
    public class ChatOverlayViewController : BSMLAutomaticViewController
    {
        private const int MaxRows = 50;

        private bool _isGraphicsDeviceStable = true;

        private void ValidateGraphicsDevice()
        {
            _isGraphicsDeviceStable = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        }


        [UIComponent("chat-container")]
        private Transform _chatContainer;

        private readonly Queue<GameObject> _rows = new Queue<GameObject>();

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            

            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);

            if (firstActivation)
            {
                var chatManager = ChatManager.GetInstance();
                chatManager.OnChatMessageReceived += HandleChatMessage;
                chatManager.OnSubscriptionReceived += HandleSub;
                chatManager.OnFollowReceived += HandleFollow;
                chatManager.OnRaidReceived += HandleRaid;
            }
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);

            if (removedFromHierarchy)
            {
                var chatManager = ChatManager.GetInstance();
                chatManager.OnChatMessageReceived -= HandleChatMessage;
                chatManager.OnSubscriptionReceived -= HandleSub;
                chatManager.OnFollowReceived -= HandleFollow;
                chatManager.OnRaidReceived -= HandleRaid;
            }
        }

    
        private void HandleChatMessage(ChatContext ctx)
        {
            // For now we show both backends; later you can filter to NativeTwitch only
            AddChatRow(ctx);
        }

        private void HandleSub(string user, int tier)
            => AddSystemRow($"[Sub x{tier}] {user}");

        private void HandleFollow(string user)
            => AddSystemRow($"[Follow] {user}");

        private void HandleRaid(string raider, int viewers)
            => AddSystemRow($"[Raid] {raider} ({viewers} viewers)");

        private void AddSystemRow(string text)
        {
            var ctx = new ChatContext
            {
                SenderName = "",
                MessageText = text
            };
            AddChatRow(ctx, isSystem: true);
        }

        private void AddChatRow(ChatContext ctx, bool isSystem = false)
        {
            if (_chatContainer == null)
                return;

            // Trim oldest row when exceeding max
            if (_rows.Count >= MaxRows)
            {
                var oldest = _rows.Dequeue();
                Destroy(oldest);
            }

            // Row root
            var rowGO = new GameObject("ChatRow");
            rowGO.transform.SetParent(_chatContainer, false);

            var hl = rowGO.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.spacing = 1.5f;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            // 1) Identity icon (role)
            var iconGO = new GameObject("RoleIcon");
            iconGO.transform.SetParent(rowGO.transform, false);
            var iconImage = iconGO.AddComponent<Image>();

            if (isSystem)
            {
                iconImage.color = new Color(0.7f, 0.7f, 0.7f); // grey
            }
            else if (ctx.IsBroadcaster)
            {
                iconImage.color = new Color(1f, 0.5f, 0f); // orange
            }
            else if (ctx.IsModerator)
            {
                iconImage.color = new Color(0f, 0.8f, 0.2f); // green
            }
            else if (ctx.IsSubscriber)
            {
                iconImage.color = new Color(0.5f, 0f, 1f); // purple
            }
            else if (ctx.IsVip)
            {
                iconImage.color = new Color(1f, 0f, 0.5f); // pink
            }
            else
            {
                iconImage.color = new Color(0.3f, 0.3f, 0.3f); // default
            }

            var iconRT = iconGO.GetComponent<RectTransform>();
            iconRT.sizeDelta = new Vector2(2.5f, 2.5f);





            // 2) Name + badges (text for now)
            var nameGO = new GameObject("Name");
            nameGO.transform.SetParent(rowGO.transform, false);
            var nameText = nameGO.AddComponent<TextMeshProUGUI>();
            nameText.fontSize = 2.8f;
            nameText.enableWordWrapping = false;

            string badgePrefix = "";
            if (ctx.IsBroadcaster) badgePrefix += "[B] ";
            else if (ctx.IsModerator) badgePrefix += "[Mod] ";
            else if (ctx.IsSubscriber) badgePrefix += "[Sub] ";
            else if (ctx.IsVip) badgePrefix += "[VIP] ";


            // BEFORE (line where nameText.text is set):
            nameText.text = isSystem ? "" : $"{badgePrefix}{ctx.SenderName}";

            // AFTER - Add this wrapper:
            try
            {
                ValidateGraphicsDevice();
                if (!_isGraphicsDeviceStable)
                {
                    Plugin.Log.Warn("ChatOverlay: Graphics device not ready, deferring text update");
                    return;
                }
                nameText.text = isSystem ? "" : $"{badgePrefix}{ctx.SenderName}";
            }
            catch (NullReferenceException ex)
            {
                Plugin.Log.Error($"ChatOverlay: TMPro error setting name text: {ex.Message}");
                return; // Skip this row
            }



            
            nameText.color = Color.white;

            // 3) Message text (with emotes as plain text for now)
            var msgGO = new GameObject("Message");
            msgGO.transform.SetParent(rowGO.transform, false);
            var msgText = msgGO.AddComponent<TextMeshProUGUI>();
            msgText.fontSize = 2.8f;
            msgText.enableWordWrapping = true;


            try
            {
                msgText.text = ctx.MessageText;
            }
            catch (NullReferenceException ex)
            {
                Plugin.Log.Error($"ChatOverlay: TMPro error setting message text: {ex.Message}");
            }

            msgText.color = isSystem ? Color.cyan : Color.white;

            _rows.Enqueue(rowGO);
        }
    }
}

