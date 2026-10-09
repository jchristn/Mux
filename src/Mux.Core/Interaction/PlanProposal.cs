namespace Mux.Core.Interaction
{
    using System.Collections.Generic;

    /// <summary>
    /// The plan the model presents through <c>exit_plan</c> at the end of plan mode: the plan as Markdown and an
    /// optional list of steps that become the task plan once approved.
    /// </summary>
    public sealed class PlanProposal
    {
        #region Public-Members

        /// <summary>The plan in Markdown. Never null.</summary>
        public string Plan { get; set; } = string.Empty;

        /// <summary>The steps, one short line each, in order. Never null; may be empty.</summary>
        public List<string> Steps { get; set; } = new List<string>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Formats the plan for display or as a prompt: the Markdown, then the steps as a numbered list when the
        /// Markdown does not already contain them.
        /// </summary>
        /// <returns>The text.</returns>
        public string ToText()
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder(Plan.Trim());
            if (Steps.Count > 0)
            {
                builder.Append("\n\nSteps:");
                for (int i = 0; i < Steps.Count; i++)
                {
                    builder.Append('\n').Append(i + 1).Append(". ").Append(Steps[i]);
                }
            }

            return builder.ToString();
        }


        /// <summary>
        /// Converts the steps into pending tasks (ids <c>1</c>, <c>2</c>, ...) for the task plan.
        /// </summary>
        /// <returns>The tasks; empty when there are no steps.</returns>
        public List<Mux.Core.Tasks.AgentTask> ToTasks()
        {
            List<Mux.Core.Tasks.AgentTask> tasks = new List<Mux.Core.Tasks.AgentTask>();
            for (int i = 0; i < Steps.Count; i++)
            {
                tasks.Add(new Mux.Core.Tasks.AgentTask { Id = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Title = Steps[i] });
            }

            return tasks;
        }

        /// <summary>
        /// The prompt that starts carrying out an approved plan.
        /// </summary>
        /// <returns>The execution prompt.</returns>
        public string ToExecutionPrompt()
        {
            return "The plan below was approved. Carry it out now, step by step, keeping the task list current with update_task, and verify the result as the plan describes.\n\n" + ToText();
        }

        #endregion
    }
}