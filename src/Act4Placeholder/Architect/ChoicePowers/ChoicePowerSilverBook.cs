using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace Act4Placeholder;

// Book choice power applied to the player when they pick a book in the Grand Library event.
// 玩家在宏伟图书馆事件中选择书籍时施加给玩家的选择标记力量。
internal sealed class ChoicePowerSilverBook : PowerModel
{
public override PowerType Type => PowerType.Buff;

public override PowerStackType StackType => PowerStackType.Counter;
}