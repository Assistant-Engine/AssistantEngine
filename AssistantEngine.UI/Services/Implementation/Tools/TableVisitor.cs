#if WINDOWS || WEB
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;
namespace AssistantEngine.Services.Implementation.Tools
{


    public class TableColumnVisitor : TSqlFragmentVisitor
    {
        public HashSet<string> TableNames { get; } = new();
        public List<(string Table, string Column)> ColumnReferences { get; } = new();

        public override void Visit(NamedTableReference node)
        {
            TableNames.Add(node.SchemaObject.BaseIdentifier.Value);
            base.Visit(node);
        }

        public override void Visit(ColumnReferenceExpression node)
        {
            // guard against null MultiPartIdentifier (e.g. COUNT(*) or other constructs)
            if (node.MultiPartIdentifier == null)
            {
                base.Visit(node);
                return;
            }

            var ids = node.MultiPartIdentifier.Identifiers;
            if (ids.Count == 2)
            {
                ColumnReferences.Add((ids[0].Value, ids[1].Value));
            }
            else if (ids.Count == 1 && TableNames.Count == 1)
            {
                ColumnReferences.Add((TableNames.First(), ids[0].Value));
            }

            base.Visit(node);
        }
    }


}
#endif