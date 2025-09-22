using AssistantEngine.UI.Services.Models.Ingestion;
using Microsoft.Extensions.VectorData;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Implementation.Ingestion.Chunks
{
    /// <summary>
    /// Non-generic façade over VectorStoreCollection to allow mixing multiple TRecord types.
    /// </summary>
    public interface IChunkStore
    {
        string Name { get; }

        // single-record upsert
        Task UpsertAsync(IIngestedChunk chunk, CancellationToken ct = default);

        // batch upsert
        Task UpsertAsync(IEnumerable<IIngestedChunk> chunks, CancellationToken ct = default);

        // delete single
        Task DeleteAsync(string key, CancellationToken ct = default);

        // delete batch
        Task DeleteAsync(IEnumerable<string> keys, CancellationToken ct = default);

        // get batch via predicate
        IAsyncEnumerable<IIngestedChunk> GetAsync(
            Expression<Func<IIngestedChunk, bool>> filter,
            int top,
            CancellationToken ct = default);
        // vector search
        Task<IReadOnlyList<IIngestedChunk>> SearchAsync<TInput>(
            TInput searchValue,
            int top,
            VectorSearchOptions<IIngestedChunk>? options = null,
            CancellationToken ct = default)
            where TInput : notnull;


        Task<IReadOnlyList<IIngestedChunk>> SearchAsync(
            string query,
            int maxResults,
            IDictionary<string, string>? metadataFilters = null,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Adapts a concrete VectorStoreCollection&lt;string,T&gt; into IChunkStore.
    /// </summary>
    public class ChunkStoreAdapter<T> : IChunkStore
        where T : class, IIngestedChunk
    {
        private readonly VectorStoreCollection<string, T> _inner;

        public ChunkStoreAdapter(VectorStoreCollection<string, T> inner) => _inner = inner;
        public string Name => _inner.Name;

        public Task UpsertAsync(IIngestedChunk chunk, CancellationToken ct = default) =>
            _inner.UpsertAsync((T)chunk, ct);

        public Task UpsertAsync(IEnumerable<IIngestedChunk> chunks, CancellationToken ct = default) =>
            _inner.UpsertAsync(chunks.OfType<T>(), ct);

        public Task DeleteAsync(string key, CancellationToken ct = default) =>
            _inner.DeleteAsync(key, ct);

        public Task DeleteAsync(IEnumerable<string> keys, CancellationToken ct = default) =>
            _inner.DeleteAsync(keys, ct);

        public IAsyncEnumerable<IIngestedChunk> GetAsync(
          Expression<Func<IIngestedChunk, bool>> filter,
          int top,
          CancellationToken ct = default)
        {
            // Rebind parameter from IIngestedChunk to T
            var innerFilter = filter.ConvertParameter<T, IIngestedChunk>();
            return _inner.GetAsync(innerFilter, top, null, ct)
                         .Select(r => (IIngestedChunk)r);
        }
        public async Task<IReadOnlyList<IIngestedChunk>> SearchAsync<TInput>(
            TInput searchValue,
            int top,
            VectorSearchOptions<IIngestedChunk>? options = null,
            CancellationToken ct = default)
            where TInput : notnull
        {
            // Convert VectorSearchOptions<IIngestedChunk> → VectorSearchOptions<T>
            VectorSearchOptions<T>? innerOptions = null;
            if (options != null)
            {
                innerOptions = new VectorSearchOptions<T>
                {
                    // Rewrap the filter expression
                    Filter = options.Filter is Expression<Func<IIngestedChunk, bool>> f
                        ? f.ConvertParameter<T, IIngestedChunk>()
                        : null
                };
            }

            var results = _inner.SearchAsync(searchValue, top, innerOptions, ct);
            return await results
                .Select(r => (IIngestedChunk)r.Record)
                .ToListAsync(ct);
        }
        public async Task<IReadOnlyList<IIngestedChunk>> SearchAsync(
    string query,
    int maxResults,
    IDictionary<string, string>? metadataFilters = null,
    CancellationToken ct = default)
        {
            Expression<Func<T, bool>>? filterExpr = null;

            if (metadataFilters?.Count > 0)
            {
                var param = Expression.Parameter(typeof(T), "record");
                Expression? body = null;

                foreach (var kv in metadataFilters)
                {
                    var pi = typeof(T).GetProperty(kv.Key);
                    if (pi == null) continue;

                    var member = Expression.Property(param, pi);
                    Expression equals;

                    // if it’s already a string, compare directly
                    if (pi.PropertyType == typeof(string))
                    {
                        var constant = Expression.Constant(kv.Value, typeof(string));
                        equals = Expression.Equal(member, constant);
                    }
                    else
                    {
                        // convert the filter string to the property’s type
                        object typedValue = Convert.ChangeType(kv.Value, pi.PropertyType);
                        var constant = Expression.Constant(typedValue, pi.PropertyType);
                        equals = Expression.Equal(member, constant);
                    }


                    body = body is null
                        ? equals
                        : Expression.AndAlso(body, equals);
                }

                if (body != null)
                    filterExpr = Expression.Lambda<Func<T, bool>>(body, param);
            }

            var options = new VectorSearchOptions<T> { Filter = filterExpr };
            var results = _inner.SearchAsync(query, maxResults, options, ct);

            
            return await results
                .Select(r => (IIngestedChunk)r.Record)
                .ToListAsync(ct);
        }
    }

    // Helper to rebind expression parameter types
    internal static class ExpressionRebinder
    {
        public static Expression<Func<TTo, bool>> ConvertParameter<TTo, TFrom>(
            this Expression<Func<TFrom, bool>> expr)
        {
            var param = Expression.Parameter(typeof(TTo), expr.Parameters[0].Name);
            var body = new ParameterReplacer(expr.Parameters[0], param).Visit(expr.Body)!;
            return Expression.Lambda<Func<TTo, bool>>(body, param);
        }

        private class ParameterReplacer : ExpressionVisitor
        {
            private readonly ParameterExpression _old, _new;
            public ParameterReplacer(ParameterExpression old, ParameterExpression @new)
                => (_old, _new) = (old, @new);

            protected override Expression VisitParameter(ParameterExpression node)
                => node == _old ? _new : base.VisitParameter(node);
        }
    }
}
