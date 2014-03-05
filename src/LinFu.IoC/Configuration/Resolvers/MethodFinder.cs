﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LinFu.Finders;
using LinFu.Finders.Interfaces;
using LinFu.IoC.Configuration.Interfaces;
using LinFu.IoC.Interfaces;

namespace LinFu.IoC.Configuration
{
    /// <summary>
    /// Represents a class that determines which method best matches the
    /// services currently in the target container.
    /// </summary>
    /// <typeparam name="T">The method type to search.</typeparam>
    public class MethodFinder<T> : IMethodFinder<T>
        where T : MethodBase
    {
        /// <summary>
        /// Determines which method best matches the
        /// services currently in the target container.
        /// </summary>
        /// <param name="items">The list of methods to search.</param>
        /// <param name="finderContext">The <see cref="IMethodFinderContext"/> that describes the target method.</param>        
        /// <returns>Returns the method with the most resolvable parameters from the target <see cref="IServiceContainer"/> instance.</returns>
        public T GetBestMatch(IEnumerable<T> items, IMethodFinderContext finderContext)
        {
            T bestMatch = null;
            var fuzzyList = items.AsFuzzyList();

            // Return the first item
            // if there is no other alternative
            if (fuzzyList.Count == 1)
                return fuzzyList[0].Item;

            var additionalArguments = finderContext.Arguments;
            var additionalArgumentTypes = (from argument in additionalArguments
                let argumentType =
                    argument == null ? typeof (object) : argument.GetType()
                select argumentType).ToList();

            Rank(fuzzyList, finderContext);

            // Match the generic parameter types
            var genericParameterCount = finderContext.TypeArguments != null ? finderContext.TypeArguments.Count() : 0;
            Func<T, bool> matchGenericArgumentCount = method =>
            {
                var genericArguments = method.IsGenericMethod
                    ? method.GetGenericArguments
                        ()
                    : new Type[0];
                var currentParameterCount = genericArguments.Count();

                return genericParameterCount == currentParameterCount;
            };
            fuzzyList.AddCriteria(matchGenericArgumentCount, CriteriaType.Critical);

            var candidates = fuzzyList.Where(fuzzy => fuzzy.Confidence > 0);
            bestMatch = SelectBestMatch(candidates);

            // If all else fails, find the method
            // that matches only the additional arguments
            if (bestMatch != null)
                return bestMatch;

            return GetNextBestMatch(fuzzyList, additionalArgumentTypes, bestMatch);
        }


        private T GetNextBestMatch(IList<IFuzzyItem<T>> fuzzyList, List<Type> additionalArgumentTypes, T bestMatch)
        {
            var additionalArgumentCount = additionalArgumentTypes.Count;
            fuzzyList.Reset();

            // Match the number of arguments
            Func<T, bool> matchParameterCount = method =>
            {
                var parameters = method.GetParameters();
                var parameterCount = parameters != null
                    ? parameters.Length
                    : 0;

                return parameterCount == additionalArgumentCount;
            };

            // Remove any methods that do not match
            // the parameter count
            fuzzyList.AddCriteria(matchParameterCount, CriteriaType.Critical);

            CheckArguments(fuzzyList, additionalArgumentTypes);
            var nextBestMatch = fuzzyList.BestMatch();

            if (nextBestMatch == null)
                return null;

            return nextBestMatch.Item;
        }

        /// <summary>
        /// Determines which item among the <paramref name="candidates"/> is the best match.
        /// </summary>
        /// <param name="candidates">The list of possible matches.</param>
        /// <returns>The best match if found; otherwise, it should return <c>null</c>.</returns>
        protected virtual T SelectBestMatch(IEnumerable<IFuzzyItem<T>> candidates)
        {
            var bestMatch = default(T);
            // Since the remaining constructors all have
            // parameter types that currently exist
            // in the container as a service,
            // the best match will be the constructor with
            // the most parameters
            var bestParameterCount = -1;
            foreach (var candidate in candidates)
            {
                var currentItem = candidate.Item;
                var parameters = currentItem.GetParameters();
                var parameterCount = parameters.Count();

                if (parameterCount <= bestParameterCount)
                    continue;

                bestMatch = currentItem;
                bestParameterCount = parameterCount;
            }
            return bestMatch;
        }

        /// <summary>
        /// Adds additional <see cref="ICriteria{T}"/> to the fuzzy search list.
        /// </summary>
        /// <param name="methods">The list of methods to rank.</param>
        /// <param name="finderContext">The <see cref="IMethodFinderContext"/> that describes the target method.</param>        
        protected virtual void Rank(IList<IFuzzyItem<T>> methods, IMethodFinderContext finderContext)
        {
        }

        /// <summary>
        /// Attempts to match the <paramref name="additionalArgumentTypes"/> against the <paramref name="fuzzyList">list of methods</paramref>.
        /// </summary>
        /// <param name="fuzzyList">The list of items currently being compared.</param>
        /// <param name="additionalArgumentTypes">The set of <see cref="Type"/> instances that describe each supplied argument type.</param>
        private static void CheckArguments(IList<IFuzzyItem<T>> fuzzyList,
            IEnumerable<Type> additionalArgumentTypes)
        {
            var argTypes = additionalArgumentTypes.ToArray();

            for (var i = 1; i <= argTypes.Length; i++)
            {
                var currentOffset = i;
                var currentIndex = argTypes.Length - currentOffset;
                var currentArgumentType = argTypes[currentIndex];
                Func<T, bool> hasCompatibleArgument = method =>
                {
                    var parameters = method.GetParameters();
                    var parameterCount = parameters.Length;
                    var targetParameterIndex = currentIndex;

                    // Make sure that the index is valid
                    if (targetParameterIndex < 0 ||
                        targetParameterIndex >= parameterCount)
                        return false;

                    // The parameter type must be compatible with the
                    // given argument type
                    var parameterType =
                        parameters[targetParameterIndex].ParameterType;
                    return parameterType.IsAssignableFrom(currentArgumentType);
                };

                // Match each additional argument type to its
                // relative position from the end of the parameter
                // list
                fuzzyList.AddCriteria(hasCompatibleArgument, CriteriaType.Critical);

                Func<T, bool> hasExactArgumentType = method =>
                {
                    var parameters = method.GetParameters();
                    var parameterCount = parameters.Length;
                    var targetParameterIndex = currentIndex;

                    // Make sure that the index is valid
                    if (targetParameterIndex < 0 ||
                        targetParameterIndex >= parameterCount)
                        return false;

                    // The parameter type should match the
                    // given argument type
                    var parameterType =
                        parameters[targetParameterIndex].ParameterType;
                    return parameterType.IsAssignableFrom(currentArgumentType);
                };

                // Make sure that the finder prefers exact
                // type matches over compatible types
                fuzzyList.AddCriteria(hasExactArgumentType, CriteriaType.Optional);
            }
        }
    }
}