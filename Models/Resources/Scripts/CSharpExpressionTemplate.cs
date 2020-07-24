using System;
using System.Collections.Generic;
using APSIM.Shared.Utilities;
using Models.Functions;
using Models;
using Models.Core;

namespace Models
{
    /// <summary>
    /// An example/template IFunction implementation.
    /// </summary>
    [Serializable]
    public class Script : Model, IFunction
    {
        //[Link] Clock Clock = null;

        /// <summary>Gets the value of the function.</summary>
        public double Value(int arrayIndex = -1)
        {
            return 123456;
        }
    }
}