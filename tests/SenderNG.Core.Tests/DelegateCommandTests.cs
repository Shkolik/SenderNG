using SenderNG.Core.Commands;
using Xunit;
public class DelegateCommandTests {
  [Fact] public void Execute_calls_action() {
    bool called=false;
    var cmd=new DelegateCommand(_=> called=true);
    Assert.True(cmd.CanExecute(null));
    cmd.Execute(null);
    Assert.True(called);
  }
  [Fact] public void CanExecute_respects_predicate() {
    var cmd=new DelegateCommand(_=> {}, _=> false);
    Assert.False(cmd.CanExecute(null));
  }
}
