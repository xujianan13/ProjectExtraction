using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>内容更新服务，负责后端生命周期、操作互斥和计划有效性。</summary>
    public sealed class ContentUpdateService : IContentUpdateService
    {
        private readonly IUpdateBackend _backend;

        private CancellationTokenSource _lifetimeCts;
        private CancellationTokenSource _activeOperationCts;
        private UpdatePlan _currentPlan;
        private int _nextPlanId = 1;
        private bool _hasCurrentPlan;
        private bool _isBusy;
        private bool _isInitialized;
        private bool _isDisposed;

        /// <summary>创建内容更新服务。</summary>
        public ContentUpdateService(IUpdateBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        /// <inheritdoc />
        public event Action<UpdateProgress> ProgressChanged;

        /// <inheritdoc />
        public void Initialize()
        {
            ThrowIfDisposed();
            if (_isInitialized) return;

            _lifetimeCts = new CancellationTokenSource();
            _backend.ProgressChanged += OnBackendProgressChanged;
            try
            {
                _backend.Initialize();
                _isInitialized = true;
            }
            catch
            {
                _backend.ProgressChanged -= OnBackendProgressChanged;
                _lifetimeCts.Dispose();
                _lifetimeCts = null;
                throw;
            }
        }

        /// <inheritdoc />
        public async UniTask<UpdatePlan> CheckAsync(
            UpdatePackageRequest request,
            CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            EnsureInitialized();

            CancellationToken operationToken = BeginOperation(ct);
            _hasCurrentPlan = false;
            _currentPlan = default;
            try
            {
                UpdatePlan plan = await _backend.CheckAsync(request, operationToken);
                operationToken.ThrowIfCancellationRequested();
                ValidateBackendPlan(request, plan);

                plan = plan.AssignId(NextPlanId());
                _currentPlan = plan;
                _hasCurrentPlan = true;
                return plan;
            }
            finally
            {
                EndOperation();
            }
        }

        /// <inheritdoc />
        public async UniTask<UpdateExecutionResult> ExecuteAsync(
            UpdatePlan plan,
            CancellationToken ct = default)
        {
            EnsureInitialized();
            if (!IsCurrentPlan(plan))
            {
                return UpdateExecutionResult.Failure(
                    plan.LocalVersion,
                    UpdateErrorCode.InvalidPlan,
                    "更新计划无效、已过期或不属于当前 ContentUpdateService。",
                    false);
            }

            CancellationToken operationToken = BeginOperation(ct);
            try
            {
                try
                {
                    return await _backend.ExecuteAsync(plan, operationToken);
                }
                catch (OperationCanceledException)
                {
                    return UpdateExecutionResult.Cancelled(plan.LocalVersion);
                }
            }
            finally
            {
                _hasCurrentPlan = false;
                _currentPlan = default;
                EndOperation();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            if (!_isInitialized) return;

            _isInitialized = false;
            _hasCurrentPlan = false;
            _currentPlan = default;
            _lifetimeCts.Cancel();
            _activeOperationCts?.Cancel();
            _backend.ProgressChanged -= OnBackendProgressChanged;
            try
            {
                _backend.Shutdown();
            }
            finally
            {
                _lifetimeCts.Dispose();
                _lifetimeCts = null;
            }
        }

        private void EnsureInitialized()
        {
            ThrowIfDisposed();
            if (!_isInitialized)
                throw new InvalidOperationException("ContentUpdateService 尚未初始化。");
        }

        private CancellationToken BeginOperation(CancellationToken ct)
        {
            if (_isBusy)
                throw new InvalidOperationException("同一时刻只允许执行一个内容更新操作。");

            _isBusy = true;
            _activeOperationCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);
            return _activeOperationCts.Token;
        }

        private void EndOperation()
        {
            _activeOperationCts?.Dispose();
            _activeOperationCts = null;
            _isBusy = false;
        }

        private int NextPlanId()
        {
            int id = _nextPlanId++;
            if (_nextPlanId <= 0) _nextPlanId = 1;
            return id;
        }

        private bool IsCurrentPlan(UpdatePlan plan)
        {
            return _hasCurrentPlan &&
                   plan.Id == _currentPlan.Id &&
                   plan.PackageName == _currentPlan.PackageName &&
                   plan.RemoteVersion == _currentPlan.RemoteVersion;
        }

        private static void ValidateBackendPlan(UpdatePackageRequest request, UpdatePlan plan)
        {
            if (string.IsNullOrEmpty(plan.PackageName))
                throw new InvalidOperationException("更新后端返回了缺少内容包名称的计划。");
            if (!string.Equals(request.PackageName, plan.PackageName, StringComparison.Ordinal))
                throw new InvalidOperationException("更新后端返回的内容包名称与请求不一致。");
        }

        private void OnBackendProgressChanged(UpdateProgress progress)
        {
            ProgressChanged?.Invoke(progress);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ContentUpdateService));
        }
    }
}
