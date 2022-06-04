using System;
using System.Threading;
using System.Threading.Tasks;
using BililiveRecorder.Core.Api.Model;
using Polly;
using Polly.Registry;

namespace BililiveRecorder.Core.Api
{
    internal class PolicyWrappedApiClient<T> : IApiClient, IDanmakuServerApiClient, IDisposable where T : class, IApiClient, IDanmakuServerApiClient, IDisposable
    {
        private readonly T client;
        private readonly IReadOnlyPolicyRegistry<string> policies;

        public PolicyWrappedApiClient(T client, IReadOnlyPolicyRegistry<string> policies)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.policies = policies ?? throw new ArgumentNullException(nameof(policies));
        }

        public async Task<BilibiliApiResponse<DanmuInfo>> GetDanmakuServerAsync(int roomid, CancellationToken cancellationToken) => await this.policies
            .Get<IAsyncPolicy>(PolicyNames.PolicyDanmakuApiRequestAsync)
            .ExecuteAsync((_, token) => this.client.GetDanmakuServerAsync(roomid, token), new Context(PolicyNames.CacheKeyDanmaku + ":" + roomid), cancellationToken)
            .ConfigureAwait(false);

        public async Task<BilibiliApiResponse<RoomInfo>> GetRoomInfoAsync(int roomid, CancellationToken cancellationToken) => await this.policies
            .Get<IAsyncPolicy>(PolicyNames.PolicyRoomInfoApiRequestAsync)
            .ExecuteAsync((_, token) => this.client.GetRoomInfoAsync(roomid, token), new Context(PolicyNames.CacheKeyRoomInfo + ":" + roomid), cancellationToken)
            .ConfigureAwait(false);

        public async Task<BilibiliApiResponse<RoomPlayInfo>> GetStreamUrlAsync(int roomid, int qn, CancellationToken cancellationToken) => await this.policies
            .Get<IAsyncPolicy>(PolicyNames.PolicyStreamApiRequestAsync)
            .ExecuteAsync((_, token) => this.client.GetStreamUrlAsync(roomid, qn, token), new Context(PolicyNames.CacheKeyStream + ":" + roomid + ":" + qn), cancellationToken)
            .ConfigureAwait(false);

        public void Dispose() => this.client.Dispose();
    }
}
