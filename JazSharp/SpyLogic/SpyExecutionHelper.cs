﻿using JazSharp.Spies;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace JazSharp.SpyLogic
{
    internal static class SpyExecutionHelper
    {
        internal static object HandleCall(object[] parameters, MethodBase originalMethod)
        {
            var method = originalMethod as MethodInfo;

            if (method == null)
            {
                throw new JazSpyException("JazSharp failed to determine the original method called before spy wrappers were applied.");
            }

            object instance = null;

            if (!method.IsStatic)
            {
                instance = parameters[0];
                parameters = parameters.Skip(1).ToArray();

                if (instance == null)
                {
                    throw new NullReferenceException();
                }
            }

            var key = method.IsStatic ? string.Empty : instance;
            var spy = Spy.Get(method, key);

            return InnerHandleCall(spy, method, instance, parameters);
        }

        internal static object InnerHandleCall(Spy spy, MethodInfo method, object instance, object[] parameters)
        {
            if (spy == null)
            {
                try
                {
                    return method.Invoke(instance, parameters);
                }
                catch (TargetInvocationException ex)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    throw;
                }
            }

            spy.CallLog.Add(ImmutableArray.CreateRange(parameters));

            if (!spy.Behaviours.Any())
            {
                throw new JazSpyException("Unexpected call to spy after last behaviour/return value.");
            }

            return spy.Behaviours.Peek().Execute(spy, method, instance, parameters);
        }
    }
}
