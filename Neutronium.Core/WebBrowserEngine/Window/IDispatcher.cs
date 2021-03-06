﻿using System;
using System.Threading.Tasks;

namespace Neutronium.Core.WebBrowserEngine.Window
{
    /// <summary>
    /// Abstraction of a dipatcher used to communication with object having thread affinity
    /// such as HMTL C# browser implementation, or UI framework (WPF) 
    /// </summary>
    public interface IDispatcher
    {
        /// <summary>
        /// Run on a safe thread the corresponding action
        /// </summary>
        /// <param name="act">
        /// Action to be executed
        /// </param>
        /// <returns>
        /// the corresponding task
        ///</returns>
        Task RunAsync(Action act);

        /// <summary>
        /// Run on a safe thread the corresponding action
        /// in a none-blocking manner.
        /// No task is return for performance optimization
        /// </summary>
        /// <param name="act">
        /// Action to be executed
        /// </param>
        void Dispatch(Action act);

        /// <summary>
        /// Run on a safe thread the corresponding action and block untill completion
        /// </summary>
        /// <param name="act">
        /// Action to be executed
        /// </param>
        void Run(Action act);

        /// <summary>
        /// Compute a function on a safe thread
        /// </summary>
        /// <param name="compute">
        /// Function to be executed
        /// </param>
        /// <returns>
        /// the corresponding task
        ///</returns>
        Task<T> EvaluateAsync<T>(Func<T> compute);

        /// <summary>
        /// Compute a function on a safe thread and wait for result
        /// </summary>
        /// <param name="compute">
        /// Function to be executed
        /// </param>
        T Evaluate<T>(Func<T> compute);

        /// <summary>
        /// True if current thread is the dispacther thread
        /// </summary>
        bool IsInContext();
    }
}
