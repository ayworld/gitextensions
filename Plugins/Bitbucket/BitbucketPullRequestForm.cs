﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GitCommands;
using GitUI;
using GitUIPluginInterfaces;
using Microsoft.VisualStudio.Threading;
using ResourceManager;

namespace Bitbucket
{
    public partial class BitbucketPullRequestForm : GitExtensionsFormBase
    {
        private readonly TranslationString _yourRepositoryIsNotInBitbucket = new TranslationString("Your repository is not hosted in Bitbucket.");
        private readonly TranslationString _commited = new TranslationString("{0} committed\n{1}");
        private readonly TranslationString _success = new TranslationString("Success");
        private readonly TranslationString _error = new TranslationString("Error");
        private readonly TranslationString _linkLabelToolTip = new TranslationString("Right-click to copy link");

        private readonly Settings _settings;
        private readonly BindingList<BitbucketUser> _reviewers = new BindingList<BitbucketUser>();

        public BitbucketPullRequestForm(BitbucketPlugin plugin, ISettingsSource settings, GitUIBaseEventArgs gitUiCommands)
        {
            InitializeComponent();
            Translate();

            _settings = Settings.Parse(gitUiCommands.GitModule, settings, plugin);
            if (_settings == null)
            {
                MessageBox.Show(_yourRepositoryIsNotInBitbucket.Text);
                Close();
                return;
            }

            Load += BitbucketViewPullRequestFormLoad;
            Load += BitbucketPullRequestFormLoad;

            lblLinkCreatePull.Text = string.Format("{0}/projects/{1}/repos/{2}/pull-requests?create",
                                      _settings.BitbucketUrl, _settings.ProjectKey, _settings.RepoSlug);
            toolTipLink.SetToolTip(lblLinkCreatePull, _linkLabelToolTip.Text);

            lblLinkViewPull.Text = string.Format("{0}/projects/{1}/repos/{2}/pull-requests",
                _settings.BitbucketUrl, _settings.ProjectKey, _settings.RepoSlug);
            toolTipLink.SetToolTip(lblLinkViewPull, _linkLabelToolTip.Text);
        }

        private void BitbucketPullRequestFormLoad(object sender, EventArgs e)
        {
            if (_settings == null)
            {
                return;
            }

            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await TaskScheduler.Default.SwitchTo(alwaysYield: true);
                var repositories = GetRepositories();

                await this.SwitchToMainThreadAsync();
                ddlRepositorySource.DataSource = repositories.ToList();
                ddlRepositoryTarget.DataSource = repositories.ToList();
                ddlRepositorySource.Enabled = true;
                ddlRepositoryTarget.Enabled = true;
            }).FileAndForget();
        }

        private void BitbucketViewPullRequestFormLoad(object sender, EventArgs e)
        {
            if (_settings == null)
            {
                return;
            }

            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await TaskScheduler.Default.SwitchTo(alwaysYield: true);
                var pullReqs = GetPullRequests();

                await this.SwitchToMainThreadAsync();
                lbxPullRequests.DataSource = pullReqs;
                lbxPullRequests.DisplayMember = "DisplayName";
            }).FileAndForget();
        }

        private List<Repository> GetRepositories()
        {
            var list = new List<Repository>();
            var getDefaultRepo = new GetRepoRequest(_settings.ProjectKey, _settings.RepoSlug, _settings);
            var defaultRepo = getDefaultRepo.Send();
            if (defaultRepo.Success)
            {
                list.Add(defaultRepo.Result);
            }

            return list;
        }

        private List<PullRequest> GetPullRequests()
        {
            var list = new List<PullRequest>();
            var getPullReqs = new GetPullRequest(_settings.ProjectKey, _settings.RepoSlug, _settings);
            var result = getPullReqs.Send();
            if (result.Success)
            {
                list.AddRange(result.Result);
            }

            return list;
        }

        private void BtnCreateClick(object sender, EventArgs e)
        {
            if (ddlBranchSource.SelectedValue == null ||
                ddlBranchTarget.SelectedValue == null ||
                ddlRepositorySource.SelectedValue == null ||
                ddlRepositoryTarget.SelectedValue == null)
            {
                return;
            }

            var info = new PullRequestInfo
            {
                Title = txtTitle.Text,
                Description = txtDescription.Text,
                SourceBranch = ddlBranchSource.SelectedValue.ToString(),
                TargetBranch = ddlBranchTarget.SelectedValue.ToString(),
                SourceRepo = (Repository)ddlRepositorySource.SelectedValue,
                TargetRepo = (Repository)ddlRepositoryTarget.SelectedValue,
                Reviewers = _reviewers
            };
            var pullRequest = new CreatePullRequestRequest(_settings, info);
            var response = pullRequest.Send();
            if (response.Success)
            {
                MessageBox.Show(_success.Text);
                BitbucketViewPullRequestFormLoad(null, null);
            }
            else
            {
                MessageBox.Show(string.Join(Environment.NewLine, response.Messages),
                    _error.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private readonly Dictionary<Repository, IEnumerable<string>> _branches = new Dictionary<Repository, IEnumerable<string>>();
        private IEnumerable<string> GetBitbucketBranches(Repository selectedRepo)
        {
            if (_branches.ContainsKey(selectedRepo))
            {
                return _branches[selectedRepo];
            }

            var list = new List<string>();
            var getBranches = new GetBranchesRequest(selectedRepo, _settings);
            var result = getBranches.Send();
            if (result.Success)
            {
                foreach (var value in result.Result["values"])
                {
                    list.Add(value["displayId"].ToString());
                }
            }

            _branches.Add(selectedRepo, list);
            return list;
        }

        private void DdlRepositorySourceSelectedValueChanged(object sender, EventArgs e)
        {
            RefreshDDLBranch(ddlBranchSource, ((ComboBox)sender).SelectedValue);
        }

        private void DdlRepositoryTargetSelectedValueChanged(object sender, EventArgs e)
        {
            RefreshDDLBranch(ddlBranchTarget, ((ComboBox)sender).SelectedValue);
        }

        private void RefreshDDLBranch(ComboBox branchComboBox, object selectedValue)
        {
            List<string> branchNames = GetBitbucketBranches((Repository)selectedValue).ToList();
            if (AppSettings.BranchOrderingCriteria == BranchOrdering.Alphabetically)
            {
                branchNames.Sort();
            }

            branchNames.Insert(0, "");
            branchComboBox.DataSource = branchNames;
        }

        private void DdlBranchSourceSelectedValueChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(ddlBranchSource.SelectedValue.ToString()))
            {
                return;
            }

            var commit = GetCommitInfo((Repository)ddlRepositorySource.SelectedValue,
                                                ddlBranchSource.SelectedValue.ToString());

            ddlBranchSource.Tag = commit;
            UpdateCommitInfo(lblCommitInfoSource, commit);
            txtTitle.Text = ddlBranchSource.SelectedValue.ToString().Replace("-", " ");
            UpdatePullRequestDescription();
        }

        private void DdlBranchTargetSelectedValueChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(ddlBranchTarget.SelectedValue.ToString()))
            {
                return;
            }

            var commit = GetCommitInfo((Repository)ddlRepositoryTarget.SelectedValue,
                                                ddlBranchTarget.SelectedValue.ToString());

            ddlBranchTarget.Tag = commit;
            UpdateCommitInfo(lblCommitInfoTarget, commit);
            UpdatePullRequestDescription();
        }

        private Commit GetCommitInfo(Repository repo, string branch)
        {
            if (repo == null || string.IsNullOrWhiteSpace(branch))
            {
                return null;
            }

            var getCommit = new GetHeadCommitRequest(repo, branch, _settings);
            var result = getCommit.Send();
            return result.Success ? result.Result : null;
        }

        private void UpdateCommitInfo(Label label, Commit commit)
        {
            if (commit == null)
            {
                label.Text = string.Empty;
            }
            else
            {
                label.Text = string.Format(_commited.Text,
                    commit.AuthorName, commit.Message);
            }
        }

        private void UpdatePullRequestDescription()
        {
            if (ddlRepositorySource.SelectedValue == null
                || ddlRepositoryTarget.SelectedValue == null
                || ddlBranchSource.Tag == null
                || ddlBranchTarget.Tag == null)
            {
                return;
            }

            var getCommitsInBetween = new GetInBetweenCommitsRequest(
                (Repository)ddlRepositorySource.SelectedValue,
                (Repository)ddlRepositoryTarget.SelectedValue,
                (Commit)ddlBranchSource.Tag,
                (Commit)ddlBranchTarget.Tag,
                _settings);

            var result = getCommitsInBetween.Send();
            if (result.Success)
            {
                var sb = new StringBuilder();
                sb.AppendLine();
                foreach (var commit in result.Result)
                {
                    if (!commit.IsMerge)
                    {
                        sb.Append("* ").AppendLine(commit.Message);
                    }
                }

                txtDescription.Text = sb.ToString();
            }
        }

        private void PullRequestChanged(object sender, EventArgs e)
        {
            var curItem = lbxPullRequests.SelectedItem as PullRequest;

            txtPRTitle.Text = curItem.Title;
            txtPRDescription.Text = curItem.Description;
            lblPRAuthor.Text = curItem.Author;
            lblPRState.Text = curItem.State;
            txtPRReviewers.Text = curItem.Reviewers;
            lblPRSourceRepo.Text = curItem.SrcDisplayName;
            lblPRSourceBranch.Text = curItem.SrcBranch;
            lblPRDestRepo.Text = curItem.DestDisplayName;
            lblPRDestBranch.Text = curItem.DestBranch;

            lblLinkViewPull.Text = string.Format("{0}/projects/{1}/repos/{2}/pull-requests/{3}/overview",
                _settings.BitbucketUrl, _settings.ProjectKey, _settings.RepoSlug, curItem.Id);
        }

        private void BtnMergeClick(object sender, EventArgs e)
        {
            if (lbxPullRequests.SelectedItem is PullRequest curItem)
            {
                var mergeInfo = new MergeRequestInfo
                {
                    Id = curItem.Id,
                    Version = curItem.Version,
                    ProjectKey = curItem.DestProjectKey,
                    TargetRepo = curItem.DestRepo,
                };

                // Merge
                var mergeRequest = new MergePullRequest(_settings, mergeInfo);
                var response = mergeRequest.Send();
                if (response.Success)
                {
                    MessageBox.Show(_success.Text);
                    BitbucketViewPullRequestFormLoad(null, null);
                }
                else
                {
                    MessageBox.Show(
                        string.Join(Environment.NewLine, response.Messages),
                        _error.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnApproveClick(object sender, EventArgs e)
        {
            if (lbxPullRequests.SelectedItem is PullRequest curItem)
            {
                var mergeInfo = new MergeRequestInfo
                {
                    Id = curItem.Id,
                    Version = curItem.Version,
                    ProjectKey = curItem.DestProjectKey,
                    TargetRepo = curItem.DestRepo,
                };

                // Approve
                var approveRequest = new ApprovePullRequest(_settings, mergeInfo);
                var response = approveRequest.Send();
                if (response.Success)
                {
                    MessageBox.Show(_success.Text);
                    BitbucketViewPullRequestFormLoad(null, null);
                }
                else
                {
                    MessageBox.Show(
                        string.Join(Environment.NewLine, response.Messages),
                        _error.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void textLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                var link = (sender as LinkLabel).Text;
                if (e.Button == MouseButtons.Right)
                {
                    // Just copy the text
                    Clipboard.SetText(link);
                }
                else
                {
                    System.Diagnostics.Process.Start(link);
                }
            }
            catch
            {
            }
        }
    }
}
