// Copyright 2012-2016 Chris Patterson
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace GreenPipes.Partitioning
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;


    public class Partitioner :
        IPartitioner
    {
        readonly IHashGenerator _hashGenerator;
        readonly string _id;
        readonly int _partitionCount;
        readonly Partition[] _partitions;

        public Partitioner(int partitionCount, IHashGenerator hashGenerator)
        {
            _id = Guid.NewGuid().ToString("N");

            _partitionCount = partitionCount;
            _hashGenerator = hashGenerator;
            _partitions = Enumerable.Range(0, partitionCount)
                .Select(index => new Partition(index))
                .ToArray();
        }

        public Task DisposeAsync(CancellationToken cancellationToken)
        {
            return Task.WhenAll(_partitions.Select(x => x.DisposeAsync(cancellationToken)));
        }

        IPartitioner<T> IPartitioner.GetPartitioner<T>(PartitionKeyProvider<T> keyProvider)
        {
            return new ContextPartitioner<T>(this, keyProvider);
        }

        public void Probe(ProbeContext context)
        {
            var scope = context.CreateScope("partitioner");
            scope.Add("id", _id);
            scope.Add("partitionCount", _partitionCount);

            for (var i = 0; i < _partitions.Length; i++)
                _partitions[i].Probe(scope);
        }

        Task Send<T>(byte[] key, T context, IPipe<T> next) where T : class, PipeContext
        {
            var hash = _hashGenerator.Hash(key);

            var partitionId = hash % _partitionCount;

            return _partitions[partitionId].Send(context, next);
        }


        class ContextPartitioner<TContext> :
            IPartitioner<TContext>
            where TContext : class, PipeContext
        {
            readonly PartitionKeyProvider<TContext> _keyProvider;
            readonly Partitioner _partitioner;

            public ContextPartitioner(Partitioner partitioner, PartitionKeyProvider<TContext> keyProvider)
            {
                _partitioner = partitioner;
                _keyProvider = keyProvider;
            }

            public Task Send(TContext context, IPipe<TContext> next)
            {
                byte[] key = _keyProvider(context);
                if (key == null)
                    throw new InvalidOperationException("The key cannot be null");

                return _partitioner.Send(key, context, next);
            }

            public void Probe(ProbeContext context)
            {
                _partitioner.Probe(context);
            }

            public Task DisposeAsync(CancellationToken cancellationToken = new CancellationToken())
            {
                return _partitioner.DisposeAsync(cancellationToken);
            }
        }
    }
}