﻿using Microsoft.TeamFoundation.WorkItemTracking.Client;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace _VSTS.DataBulkEditor.Engine
{
    public class WorkItemMigrationResetContext : MigrationContextBase
    {
        public override string Name
        {
            get
            {
                return "WorkItemMigrationResetContext";
            }
        }

        public WorkItemMigrationResetContext(MigrationEngine me) : base(me)
        {

        }

        internal override void InternalExecute()
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            //////////////////////////////////////////////////
            WorkItemStoreContext sourceStore = new WorkItemStoreContext(me.Source, WorkItemStoreFlags.BypassRules);
            TfsQueryContext tfsqc = new TfsQueryContext(sourceStore);
            tfsqc.AddParameter("TeamProject", me.Source.Name);
            tfsqc.Query = @"SELECT [System.Id], [TfsMigrationTool.ReflectedWorkItemId] FROM WorkItems WHERE  [System.TeamProject] = @TeamProject AND [TfsMigrationTool.ReflectedWorkItemId] <> '' AND [System.WorkItemType] IN ('Shared Steps', 'Shared Parameter', 'Test Case', 'Requirement', 'Task', 'User Story', 'Bug') ORDER BY [System.ChangedDate] desc "; 
            WorkItemCollection sourceWIS = tfsqc.Execute();
            Trace.WriteLine(string.Format("Source {0} work items?", sourceWIS.Count));
            //////////////////////////////////////////////////
            WorkItemStoreContext targetStore = new WorkItemStoreContext(me.Target, WorkItemStoreFlags.BypassRules);
            Project destProject = targetStore.GetProject();
            TfsQueryContext tfstqc = new TfsQueryContext(targetStore);
            tfstqc.AddParameter("TeamProject", me.Target.Name);
            tfstqc.Query = @"SELECT [System.Id], [TfsMigrationTool.ReflectedWorkItemId] FROM WorkItems WHERE  [System.TeamProject] = @TeamProject AND [TfsMigrationTool.ReflectedWorkItemId] <> '' AND [System.WorkItemType] IN ('Shared Steps', 'Shared Parameter', 'Test Case', 'Requirement', 'Task', 'User Story', 'Bug') ORDER BY [System.ChangedDate] desc ";
            WorkItemCollection targetWIS = tfstqc.Execute();
            Trace.WriteLine(string.Format("Target {0} work items?", targetWIS.Count));
            int current = sourceWIS.Count;
            int count = 0;
            long elapsedms = 0;
            foreach (WorkItem sourceWI in sourceWIS)
            {
                Stopwatch witstopwatch = new Stopwatch();
                witstopwatch.Start();
                int wid = targetStore.GetReflectedWorkItemId(sourceWI, me.ReflectedWorkItemIdFieldName);
                int windex = targetWIS.IndexOf(wid);
                WorkItem targetFound = targetWIS[windex];
                if (targetFound == null)
                {
                    Console.WriteLine("Reseting Source: {0}", sourceWI.Id);
                    sourceWI.Fields["TfsMigrationTool.ReflectedWorkItemId"].Value = "";
                    sourceWI.Save();
                }
                else
                {
                    Console.WriteLine("Target Exists for: {0}", sourceWI.Id);
                }
                witstopwatch.Stop();
                elapsedms = elapsedms + witstopwatch.ElapsedMilliseconds;
                current--;
                count++;
                TimeSpan average = new TimeSpan(0, 0, 0, 0, (int)(elapsedms / count));
                TimeSpan remaining = new TimeSpan(0, 0, 0, 0, (int)(average.TotalMilliseconds * current));
                Trace.WriteLine(string.Format("Average time of {0} per work item and {1} estimated to completion", string.Format(@"{0:s\:fff} seconds", average), string.Format(@"{0:%h} hours {0:%m} minutes {0:s\:fff} seconds", remaining)));
            }
            //////////////////////////////////////////////////
            stopwatch.Stop();
            Console.WriteLine(@"DONE in {0:%h} hours {0:%m} minutes {0:s\:fff} seconds", stopwatch.Elapsed);
        }


        private static bool HasChildPBI(WorkItem sourceWI)
        {
            return sourceWI.Title.ToLower().StartsWith("epic") || sourceWI.Title.ToLower().StartsWith("theme");
        }

        private static WorkItem CreateAndPopulateWorkItem(WorkItem oldWi, Project destProject, String destType)
        {
            bool except = false;
            Trace.Write("... Building");
            List<String> ignore = new List<string>();
            ignore.Add("System.CreatedDate");
            ignore.Add("System.CreatedBy");
            ignore.Add("System.Rev");
            ignore.Add("System.AreaId");
            ignore.Add("System.IterationId");
            ignore.Add("System.Id");
            ignore.Add("System.ChangedDate");
            ignore.Add("System.ChangedBy");
            ignore.Add("System.RevisedDate");
            ignore.Add("System.AttachedFileCount");
            ignore.Add("System.TeamProject");
            ignore.Add("System.NodeName");
            ignore.Add("System.RelatedLinkCount");
            ignore.Add("System.WorkItemType");
            ignore.Add("Microsoft.VSTS.Common.ActivatedDate");
            ignore.Add("Microsoft.VSTS.Common.StateChangeDate");
            ignore.Add("System.ExternalLinkCount");
            ignore.Add("System.HyperLinkCount");
            ignore.Add("System.Watermark");
            ignore.Add("System.AuthorizedDate");
            ignore.Add("System.BoardColumn");
            ignore.Add("System.BoardColumnDone");
            ignore.Add("System.BoardLane");
            ignore.Add("SLB.SWT.DateOfClientFeedback");


            // WorkItem newwit = oldWi.Copy(destProject.WorkItemTypes[destType]);
            WorkItem newwit = destProject.WorkItemTypes[destType].NewWorkItem();
            newwit.Title = oldWi.Title;
            newwit.State = oldWi.State;
            switch (newwit.State)
            {
                case "Done":
                    newwit.Fields["Microsoft.VSTS.Common.ClosedDate"].Value = DateTime.Now;
                    break;
                case "Closed":
                    newwit.Fields["Microsoft.VSTS.Common.ClosedDate"].Value = DateTime.Now;
                    break;
                default:
                    break;
            }
            newwit.Reason = oldWi.Reason;
            foreach (Field f in oldWi.Fields)
            {
                if (newwit.Fields.Contains(f.ReferenceName) && !ignore.Contains(f.ReferenceName))
                {
                    newwit.Fields[f.ReferenceName].Value = oldWi.Fields[f.ReferenceName].Value;
                }
            }
            newwit.AreaPath = string.Format(@"{0}\{1}", newwit.Project.Name,oldWi.AreaPath);
            newwit.IterationPath = string.Format(@"{0}\{1}", newwit.Project.Name, oldWi.IterationPath);
            newwit.Fields["System.ChangedDate"].Value = oldWi.Fields["System.ChangedDate"].Value;

            switch (destType)
            {
                case "Test Case":
                    newwit.Fields["Microsoft.VSTS.TCM.Steps"].Value = oldWi.Fields["Microsoft.VSTS.TCM.Steps"].Value;
                    newwit.Fields["Microsoft.VSTS.Common.Priority"].Value = oldWi.Fields["Microsoft.VSTS.Common.Priority"].Value;
                    break;
                //case "User Story":
                    //newwit.Fields["COMPANY.DEVISION.Analysis"].Value = oldWi.Fields["COMPANY.PRODUCT.AcceptanceCriteria"].Value;
                    //break;
                default:
                    break;
            }



            if (newwit.Fields.Contains("Microsoft.VSTS.Common.BacklogPriority")
                && newwit.Fields["Microsoft.VSTS.Common.BacklogPriority"].Value != null
                && !isNumeric(newwit.Fields["Microsoft.VSTS.Common.BacklogPriority"].Value.ToString(),
                NumberStyles.Any))
            {
                newwit.Fields["Microsoft.VSTS.Common.BacklogPriority"].Value = 10;
            }

            StringBuilder description = new StringBuilder();
            description.Append(oldWi.Description);
            description.AppendLine();
            description.AppendLine();
            description.AppendFormat("##REF##{0}##", oldWi.Id);
            newwit.Description = description.ToString();

            StringBuilder history = new StringBuilder();
            BuildCommentTable(oldWi, history);
            BuildFieldTable(oldWi, history);
            history.Append("<p>Migrated by <a href='http://nkdagility.com'>naked Agility Limited's</a> open source <a href='https://github.com/nkdAgility/VstsMigrator'>VSTS/TFS Migrator</a>.</p>");
            newwit.History = history.ToString();

            if (except)
            {
                Trace.Write("...buildErrors");
                System.Threading.Thread.Sleep(1000);

            }
            else
            {
                Trace.Write("...buildComplete");
            }

            return newwit;
        }

        private static string ReplaceFirstOccurence(string wordToReplace, string replaceWith, string input)
        {
            Regex r = new Regex(wordToReplace, RegexOptions.IgnoreCase);
            return r.Replace(input, replaceWith, 1);
        }


        private static void BuildFieldTable(WorkItem oldWi, StringBuilder history)
        {
            history.Append("<p>&nbsp;</p>");
            history.Append("<table border='1' cellpadding='2' style='width:100%;border-color:#C0C0C0;'><tr><td><b>Field</b></td><td><b>Value</b></td></tr>");
            foreach (Field f in oldWi.Fields)
            {
                if (f.Value == null)
                {
                    history.AppendFormat("<tr><td style='text-align:right;white-space:nowrap;'><b>{0}</b></td><td>n/a</td></tr>", f.Name);

                }
                else
                {
                    history.AppendFormat("<tr><td style='text-align:right;white-space:nowrap;'><b>{0}</b></td><td style='width:100%'>{1}</td></tr>", f.Name, f.Value.ToString());
                }

            }
            history.Append("</table>");
            history.Append("<p>&nbsp;</p>");
        }

        private static void BuildCommentTable(WorkItem oldWi, StringBuilder history)
        {
            history.Append("<p>&nbsp;</p>");
            history.Append("<table border='1' style='width:100%;border-color:#C0C0C0;'>");
            foreach (Revision r in oldWi.Revisions)
            {
                if (r.Fields["System.History"].Value != "" && r.Fields["System.ChangedBy"].Value != "Martin Hinshelwood (Adm)")
                {
                    r.WorkItem.Open();
                    history.AppendFormat("<tr><td style='align:right;width:100%'><p><b>{0} on {1}:</b></p><p>{2}</p></td></tr>", r.Fields["System.ChangedBy"].Value, DateTime.Parse(r.Fields["System.ChangedDate"].Value.ToString()).ToLongDateString(), r.Fields["System.History"].Value);
                }
            }
            history.Append("</table>");
            history.Append("<p>&nbsp;</p>");
        }

        static bool isNumeric(string val, NumberStyles NumberStyle)
        {
            Double result;
            return Double.TryParse(val, NumberStyle,
                System.Globalization.CultureInfo.CurrentCulture, out result);
        }


    }
}