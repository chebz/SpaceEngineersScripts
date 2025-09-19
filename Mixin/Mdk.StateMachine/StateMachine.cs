namespace IngameScript
{
    public interface IState
    {
        #region Methods
        void Enter();
        void Execute();
        #endregion
    }

    public abstract class State<T> : IState where T : Context
    {
        #region Fields
        protected T _context;
        #endregion

        #region Constructors
        public State(T context)
        {
            _context = context;
        }
        #endregion

        #region IState Members
        public virtual void Enter() { }

        public virtual void Execute() { }
        #endregion
    }

    public class Context
    {
        #region Properties
        public IState CurrentState { get; private set; }
        #endregion

        #region Methods
        public void TransitionTo(IState state)
        {
            CurrentState = state;
            if (CurrentState != null)
            {
                CurrentState.Enter();
            }
        }

        public void Execute()
        {
            if (CurrentState != null)
            {
                CurrentState.Execute();
            }
        }
        #endregion
    }
}