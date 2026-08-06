/// <summary>
/// 敌人 AI 状态接口。
/// 状态不持有 Unity 组件引用，通过 Enter/Update 传入的 EnemyStateMachine 访问各行为组件。
/// 状态只做决策（巡逻 / 战斗 / 切换），具体移动、瞄准、射击由对应控制器完成。
/// </summary>
public interface IEnemyState
{
    /// <summary>进入状态：配置需要启用的行为组件。</summary>
    void Enter(EnemyStateMachine stateMachine);

    /// <summary>每帧更新：执行当前状态的行为与切换判定。</summary>
    void Update(EnemyStateMachine stateMachine);

    /// <summary>退出状态：清理（可选，通常交给下一个状态的 Enter 处理）。</summary>
    void Exit(EnemyStateMachine stateMachine);
}
