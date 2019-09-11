using System.Runtime.CompilerServices;
using GraphQL.Types;
using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Explorer.GraphTypes;

namespace Libplanet.Explorer.Interfaces
{
    public interface IBlockChainContext<T>
        where T : IAction, new()
    {
        BlockChain<T> BlockChain { get; }
    }

    public static class BlockChainContext
    {
        private static ConditionalWeakTable<object, Schema> _schemaObjects =
            new ConditionalWeakTable<object, Schema>();

        public static Schema GetSchema<T>(this IBlockChainContext<T> context)
            where T : IAction, new()
        {
            return _schemaObjects.GetValue(
                context,
                (_) =>
                {
                    var s = new Schema { Query = new BlocksQuery<T>(context.BlockChain) };
                    return s;
                }
            );
        }
    }
}
