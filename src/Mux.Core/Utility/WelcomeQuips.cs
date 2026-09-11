namespace Mux.Core.Utility
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A collection of short, friendly welcome quips shown on the empty "start a new conversation" screen in
    /// both the desktop app and the TUI. <see cref="Next"/> returns a random one; <see cref="Pick"/> takes a
    /// caller-supplied <see cref="Random"/> for deterministic selection in tests.
    /// </summary>
    public static class WelcomeQuips
    {
        private static readonly string[] Quips =
        {
            "Mux is here to help!",
            "What can mux do for you?",
            "Let's build something great.",
            "Ready when you are.",
            "What are we shipping today?",
            "Ask mux anything.",
            "Point mux at a problem.",
            "Your coding sidekick awaits.",
            "Let's write some code.",
            "What's on your mind?",
            "Tell mux what you need.",
            "Let's get to work.",
            "Where should we start?",
            "Mux at your service.",
            "Bugs, beware.",
            "Let's untangle this together.",
            "What are we fixing today?",
            "Describe it and mux will do it.",
            "Big idea or tiny tweak?",
            "Let's make it happen.",
            "The terminal is yours.",
            "What would you like to explore?",
            "Ready to dig in.",
            "Let's ship it.",
            "From idea to implementation.",
            "Give mux a task.",
            "Let's refactor something.",
            "What's the mission?",
            "Mux is listening.",
            "Type away.",
            "Let's solve it.",
            "Your wish, mux's command.",
            "What can we automate today?",
            "Let's read some code.",
            "Point, ask, done.",
            "Ready to reason.",
            "Let's tackle the backlog.",
            "One prompt away from progress.",
            "What's blocking you?",
            "Let mux take the first pass.",
            "Coding, faster together.",
            "What shall we create?",
            "Let's make it work.",
            "Give mux the hard part.",
            "Let's chase down that bug.",
            "New session, fresh start.",
            "What's the plan?",
            "Let's dive into the codebase.",
            "Ask big. Ask small. Just ask.",
            "Mux loves a good challenge.",
            "Let's turn ideas into commits.",
            "What do you want to understand?",
            "Ready to pair up.",
            "Let's make progress.",
            "Tell mux the goal.",
            "Where does it hurt? Let's fix it.",
            "Let's explore the unknown.",
            "What are we automating?",
            "The blank page ends here.",
            "Let's get building.",
            "Mux is warmed up.",
            "Your next step starts here.",
            "What can we improve today?",
            "Let's write, test, ship.",
            "Give mux a starting point.",
            "Let's decode this together.",
            "What's today's quest?",
            "Ready to run.",
            "Let mux handle the boilerplate.",
            "Let's make something useful.",
            "Ask mux to explain it.",
            "Ask mux to build it.",
            "Ask mux to fix it.",
            "Let's clean up this code.",
            "What are we prototyping?",
            "Mux has your back.",
            "Let's turn TODOs into done.",
            "What's the next feature?",
            "Let's read, reason, resolve.",
            "Small ask or grand plan?",
            "Let's do the thing.",
            "Mux is ready to roll.",
            "What are we launching?",
            "Let's make the tests pass.",
            "Point mux at the repo.",
            "Let's find that edge case.",
            "Ready for your first prompt.",
            "Let's get unstuck.",
            "What's the itch to scratch?",
            "Let mux draft it for you.",
            "Let's build it right.",
            "Every great build starts with a prompt.",
            "What can mux clarify?",
            "Let's tame the complexity.",
            "Ready to reason it out.",
            "Give mux the details.",
            "Let's ship something today.",
            "What are we debugging?",
            "Mux is all ears.",
            "Let's make your day easier.",
            "What would help you most?",
            "Let's get coding.",
        };

        /// <summary>
        /// All welcome quips, in declaration order.
        /// </summary>
        public static IReadOnlyList<string> All => Quips;

        /// <summary>
        /// Returns a random welcome quip using the shared thread-safe random source.
        /// </summary>
        /// <returns>A welcome quip.</returns>
        public static string Next()
        {
            return Pick(Random.Shared);
        }

        /// <summary>
        /// Returns a random welcome quip using the supplied random source.
        /// </summary>
        /// <param name="random">The random source. Required.</param>
        /// <returns>A welcome quip.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is null.</exception>
        public static string Pick(Random random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            return Quips[random.Next(Quips.Length)];
        }
    }
}
