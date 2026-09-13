namespace Mux.Desktop.I18n
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Desktop-specific chrome strings that have no equivalent in the shared <c>mux serve</c> dashboard I18N
    /// packs (splash, sidebar, workspace empty state, composer, About window, chat role labels). Short chrome
    /// strings are translated across all supported locales; long descriptive/help text is kept English only
    /// (resolved through the English fallback), mirroring the dashboard's own translated/untranslated
    /// boundary. Merged with <see cref="DashboardStrings"/> by <see cref="LocalizationService"/>.
    /// </summary>
    internal static class DesktopStrings
    {
        /// <summary>
        /// Build the per-locale (key → value) catalogs for the desktop-specific strings. English carries every
        /// key (including the English-only long text); other locales carry only the translated short chrome.
        /// </summary>
        /// <returns>A dictionary of locale code to its (key → value) map.</returns>
        internal static Dictionary<string, Dictionary<string, string>> Build()
        {
            Dictionary<string, Dictionary<string, string>> all =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            // English — the complete set, including the English-only long text and the "mux" brand.
            all["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StringKeys.AppTitle, "mux" },
                { StringKeys.AppTagline, "Your AI agent, your models, your infrastructure." },
                { StringKeys.SplashLoading, "Starting…" },
                { StringKeys.Conversations, "Conversations" },
                { StringKeys.NewConversation, "New conversation" },
                { StringKeys.New, "New" },
                { StringKeys.Refresh, "Refresh" },
                { StringKeys.EmptyStateTitle, "Start a new conversation" },
                { StringKeys.EmptyStateBody, "Your threads appear here. Start a conversation, then type a prompt and press Enter." },
                { StringKeys.ComposerPlaceholder, "Type a message…" },
                { StringKeys.Send, "Send" },
                { StringKeys.Stop, "Stop" },
                { StringKeys.ChatThinking, "Thinking…" },
                { StringKeys.ChatYou, "You" },
                { StringKeys.ChatAssistant, "Assistant" },
                { StringKeys.AboutHelp, "About" },
                { StringKeys.HelpHeading, "Getting started" },
                {
                    StringKeys.HelpBody,
                    "mux Desktop runs the mux agent locally against the model and backend you configure. "
                        + "Create a conversation to start a thread, then type a prompt and press Enter. "
                        + "Endpoints, tools, MCP servers, skills, and usage analytics are managed in the app."
                },
                { StringKeys.License, "MIT License" }
            };

            all["es"] = Short("Iniciando…", "Conversaciones", "Nueva conversación", "Nueva", "Actualizar",
                "Inicia una nueva conversación", "Tu agente de IA, tus modelos, tu infraestructura.",
                "Escribe un mensaje…", "Enviar", "Detener", "Pensando…", "Tú", "Asistente",
                "Acerca de", "Primeros pasos", "Licencia MIT");

            all["pt"] = Short("Iniciando…", "Conversas", "Nova conversa", "Nova", "Atualizar",
                "Inicie uma nova conversa", "Seu agente de IA, seus modelos, sua infraestrutura.",
                "Digite uma mensagem…", "Enviar", "Parar", "Pensando…", "Você", "Assistente",
                "Sobre", "Primeiros passos", "Licença MIT");

            all["fr"] = Short("Démarrage…", "Conversations", "Nouvelle conversation", "Nouvelle", "Actualiser",
                "Démarrer une nouvelle conversation", "Votre agent IA, vos modèles, votre infrastructure.",
                "Écrivez un message…", "Envoyer", "Arrêter", "Réflexion…", "Vous", "Assistant",
                "À propos", "Prise en main", "Licence MIT");

            all["it"] = Short("Avvio…", "Conversazioni", "Nuova conversazione", "Nuova", "Aggiorna",
                "Inizia una nuova conversazione", "Il tuo agente IA, i tuoi modelli, la tua infrastruttura.",
                "Scrivi un messaggio…", "Invia", "Ferma", "Sto pensando…", "Tu", "Assistente",
                "Informazioni", "Per iniziare", "Licenza MIT");

            all["de"] = Short("Starten…", "Unterhaltungen", "Neue Unterhaltung", "Neu", "Aktualisieren",
                "Neue Unterhaltung beginnen", "Dein KI-Agent, deine Modelle, deine Infrastruktur.",
                "Nachricht eingeben…", "Senden", "Stopp", "Denkt nach…", "Du", "Assistent",
                "Über", "Erste Schritte", "MIT-Lizenz");

            all["zh"] = Short("正在启动…", "对话", "新对话", "新建", "刷新",
                "开始新对话", "你的 AI 智能体，你的模型，你的基础设施。",
                "输入消息…", "发送", "停止", "思考中…", "你", "助手",
                "关于", "快速入门", "MIT 许可证");

            all["ar"] = Short("جارٍ البدء…", "المحادثات", "محادثة جديدة", "جديد", "تحديث",
                "ابدأ محادثة جديدة", "وكيل الذكاء الاصطناعي الخاص بك، نماذجك، بنيتك التحتية.",
                "اكتب رسالة…", "إرسال", "إيقاف", "يفكر…", "أنت", "المساعد",
                "حول", "البدء", "رخصة MIT");

            all["ru"] = Short("Запуск…", "Беседы", "Новая беседа", "Создать", "Обновить",
                "Начните новую беседу", "Ваш ИИ-агент, ваши модели, ваша инфраструктура.",
                "Введите сообщение…", "Отправить", "Остановить", "Думает…", "Вы", "Ассистент",
                "О программе", "Начало работы", "Лицензия MIT");

            all["ms"] = Short("Memulakan…", "Perbualan", "Perbualan baharu", "Baharu", "Muat semula",
                "Mulakan perbualan baharu", "Ejen AI anda, model anda, infrastruktur anda.",
                "Taip mesej…", "Hantar", "Berhenti", "Sedang berfikir…", "Anda", "Pembantu",
                "Perihal", "Bermula", "Lesen MIT");

            all["hi"] = Short("प्रारंभ हो रहा है…", "बातचीत", "नई बातचीत", "नया", "ताज़ा करें",
                "नई बातचीत शुरू करें", "आपका AI एजेंट, आपके मॉडल, आपका इंफ्रास्ट्रक्चर।",
                "संदेश लिखें…", "भेजें", "रोकें", "सोच रहा है…", "आप", "सहायक",
                "परिचय", "शुरुआत करें", "MIT लाइसेंस");

            return all;
        }

        // Assemble one locale's short-chrome map from positional translations, in a fixed order.
        private static Dictionary<string, string> Short(
            string splashLoading, string conversations, string newConversation, string newLabel, string refresh,
            string emptyTitle, string tagline, string composerPlaceholder, string send, string stop,
            string thinking, string you, string assistant, string aboutHelp, string helpHeading, string license)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StringKeys.SplashLoading, splashLoading },
                { StringKeys.Conversations, conversations },
                { StringKeys.NewConversation, newConversation },
                { StringKeys.New, newLabel },
                { StringKeys.Refresh, refresh },
                { StringKeys.EmptyStateTitle, emptyTitle },
                { StringKeys.AppTagline, tagline },
                { StringKeys.ComposerPlaceholder, composerPlaceholder },
                { StringKeys.Send, send },
                { StringKeys.Stop, stop },
                { StringKeys.ChatThinking, thinking },
                { StringKeys.ChatYou, you },
                { StringKeys.ChatAssistant, assistant },
                { StringKeys.AboutHelp, aboutHelp },
                { StringKeys.HelpHeading, helpHeading },
                { StringKeys.License, license }
            };
        }
    }
}
