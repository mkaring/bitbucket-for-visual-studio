﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using GitClientVS.Contracts.Events;
using GitClientVS.Contracts.Interfaces;
using GitClientVS.Contracts.Interfaces.Services;
using GitClientVS.Contracts.Interfaces.ViewModels;
using GitClientVS.Contracts.Models;
using GitClientVS.Contracts.Models.GitClientModels;
using GitClientVS.Infrastructure.Extensions;
using ParseDiff;
using ReactiveUI;

namespace GitClientVS.Infrastructure.ViewModels
{
    [Export(typeof(IPullRequestsDetailViewModel))]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class PullRequestsDetailViewModel : ViewModelBase, IPullRequestsDetailViewModel
    {
        private readonly IGitClientService _gitClientService;
        private readonly IUserInformationService _userInformationService;
        private readonly ITreeStructureGenerator _treeStructureGenerator;
        private string _errorMessage;
        private bool _isLoading;
        private ReactiveCommand<Unit> _initializeCommand;
        private ReactiveCommand<Unit> _approveCommand;
        private ReactiveCommand<Unit> _disapproveCommand;
        private ReactiveCommand<Unit> _declineCommand;
        private ReactiveCommand<Unit> _mergeCommand;
        private GitPullRequest _pullRequest;
        private string _mainSectionCommandText;
        private bool _isApproveAvailable;
        private bool _isApproved;
        private Theme _currentTheme;


        public string PageTitle => "Pull Request Details";

        public bool IsApproveAvailable
        {
            get { return _isApproveAvailable; }
            set { this.RaiseAndSetIfChanged(ref _isApproveAvailable, value); }
        }

        public bool IsApproved
        {
            get { return _isApproved; }
            set { this.RaiseAndSetIfChanged(ref _isApproved, value); }
        }

        public string MainSectionCommandText
        {
            get { return _mainSectionCommandText; }
            set { this.RaiseAndSetIfChanged(ref _mainSectionCommandText, value); }
        }

        public GitPullRequest PullRequest
        {
            get { return _pullRequest; }
            set { this.RaiseAndSetIfChanged(ref _pullRequest, value); }
        }

        public Theme CurrentTheme
        {
            get { return _currentTheme; }
            set { this.RaiseAndSetIfChanged(ref _currentTheme, value); }
        }

        public List<KeyValuePair<string, IReactiveCommand>> ActionCommands { get; set; }

        public PullRequestDiffModel PullRequestDiffModel { get; set; }


        [ImportingConstructor]
        public PullRequestsDetailViewModel(
            IGitClientService gitClientService,
            IGitService gitService,
            ICommandsService commandsService,
            IUserInformationService userInformationService,
            IEventAggregatorService eventAggregatorService,
            ITreeStructureGenerator treeStructureGenerator
            )
        {
            _gitClientService = gitClientService;
            _userInformationService = userInformationService;
            _treeStructureGenerator = treeStructureGenerator;
            CurrentTheme = userInformationService.CurrentTheme;
            PullRequestDiffModel = new PullRequestDiffModel(commandsService);

            eventAggregatorService.GetEvent<ThemeChangedEvent>().Subscribe(ev =>
            {
                CurrentTheme = ev.Theme;
                PullRequestDiffModel.CommentTree = _treeStructureGenerator.CreateCommentTree(PullRequestDiffModel.Comments.ToList(), CurrentTheme).ToList();
            });
            this.WhenAnyValue(x => x.PullRequest).Subscribe(_ => this.RaisePropertyChanged(nameof(PageTitle)));
            this.WhenAnyObservable(x => x._approveCommand, x => x._declineCommand, x => x._disapproveCommand, x => x._mergeCommand)
                .Subscribe(_ => _initializeCommand.Execute(PullRequest.Id));
        }

        public ICommand InitializeCommand => _initializeCommand;

        public void InitializeCommands()
        {
            _initializeCommand = ReactiveCommand.CreateAsyncTask(Observable.Return(true), x => LoadPullRequestData((long)x));
            _approveCommand = ReactiveCommand.CreateAsyncTask(Observable.Return(true), async _ => { await _gitClientService.ApprovePullRequest(PullRequest.Id); });
            _disapproveCommand = ReactiveCommand.CreateAsyncTask(Observable.Return(true), async _ => { await _gitClientService.DisapprovePullRequest(PullRequest.Id); });
            _declineCommand = ReactiveCommand.CreateAsyncTask(Observable.Return(true), async _ => { await _gitClientService.DeclinePullRequest(PullRequest.Id, PullRequest.Version); });
            _mergeCommand = ReactiveCommand.CreateAsyncTask(Observable.Return(true), _ => MergePullRequest());

            ActionCommands = new List<KeyValuePair<string, IReactiveCommand>>()
            {
               new KeyValuePair<string, IReactiveCommand>("Merge",_mergeCommand),
               new KeyValuePair<string, IReactiveCommand>("Decline",_declineCommand),
               new KeyValuePair<string, IReactiveCommand>("Approve",_approveCommand),
               new KeyValuePair<string, IReactiveCommand>("Unapprove",_disapproveCommand),
            };

        }



        private async Task LoadPullRequestData(long id)
        {
            var tasks = new[]
            {
                GetPullRequestInfo(id),
                CreateCommits(id),
                CreateComments(id),
                CreateDiffContent(id)
            };

            await Task.WhenAll(tasks);
        }

        private async Task GetPullRequestInfo(long id)
        {
            PullRequest = await _gitClientService.GetPullRequest(id);
            CheckReviewers();
        }

        private async Task CreateComments(long id)
        {
            PullRequestDiffModel.Comments = (await _gitClientService.GetPullRequestComments(id)).Where(comment => comment.IsFile == false).ToList();
            PullRequestDiffModel.CommentTree = _treeStructureGenerator.CreateCommentTree(PullRequestDiffModel.Comments.ToList(), CurrentTheme).ToList();
        }

        private async Task CreateCommits(long id)
        {
            PullRequestDiffModel.Commits = (await _gitClientService.GetPullRequestCommits(id)).ToList();
        }

        private async Task CreateDiffContent(long id)
        {
            var fileDiffs = (await _gitClientService.GetPullRequestDiff(id)).ToList();
            PullRequestDiffModel.FilesTree = _treeStructureGenerator.CreateFileTree(fileDiffs).ToList();
            PullRequestDiffModel.FileDiffs = fileDiffs;
        }

        private void CheckReviewers()
        {
            IsApproved = true;
            foreach (var reviewer in PullRequest.Reviewers)
            {
                if (reviewer.Key.Username == _userInformationService.ConnectionData.UserName)
                {
                    IsApproveAvailable = true;
                    IsApproved = reviewer.Value;
                }
            }
        }

        private async Task MergePullRequest()
        {
            var gitMergeRequest = new GitMergeRequest()
            {
                CloseSourceBranch = PullRequest.CloseSourceBranch,
                Id = PullRequest.Id,
                MergeStrategy = "merge_commit",
                Version = PullRequest.Version
            };

            await _gitClientService.MergePullRequest(gitMergeRequest);
        }

        public IEnumerable<IReactiveCommand> ThrowableCommands => new[] { _initializeCommand, _mergeCommand, _approveCommand, _disapproveCommand, _declineCommand };
        public IEnumerable<IReactiveCommand> LoadingCommands => new[] { _initializeCommand, _mergeCommand, _approveCommand, _disapproveCommand, _declineCommand };

        public string ErrorMessage
        {
            get { return _errorMessage; }
            set
            {
                this.RaiseAndSetIfChanged(ref _errorMessage, value);
            }
        }

        public bool IsLoading
        {
            get { return _isLoading; }
            set { this.RaiseAndSetIfChanged(ref _isLoading, value); }
        }
    }
}
