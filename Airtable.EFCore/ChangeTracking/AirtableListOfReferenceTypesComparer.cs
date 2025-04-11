// This class is derived in large part from `ListOfReferenceTypesComparer`:
// https://github.com/dotnet/efcore/blob/30eb1fbb88f6bc7fe31217f83933122a2a403e59/src/EFCore/ChangeTracking/ListOfReferenceTypesComparer.cs
//
//
// The relevant license is provided below:
//
// The MIT License (MIT)
//
// Copyright (c) .NET Foundation and Contributors
//
// All rights reserved.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Collections;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Airtable.EFCore.ChangeTracking;

public sealed class AirtableListOfReferenceTypesComparer<TConcreteList, TElement> : ValueComparer<object>, IInfrastructure<ValueComparer>
    where TElement : class
{
    private static readonly bool IsArray = typeof(TConcreteList).IsArray;

    private static readonly bool IsReadOnly = IsArray
        || (typeof(TConcreteList).IsGenericType
            && typeof(TConcreteList).GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>));

    private static readonly MethodInfo CompareMethod = typeof(AirtableListOfReferenceTypesComparer<TConcreteList, TElement>).GetMethod(
        nameof(Compare), BindingFlags.Static | BindingFlags.NonPublic, [typeof(object), typeof(object), typeof(Func<TElement, TElement, bool>)])!;

    private static readonly MethodInfo GetHashCodeMethod = typeof(AirtableListOfReferenceTypesComparer<TConcreteList, TElement>).GetMethod(
        nameof(GetHashCode), BindingFlags.Static | BindingFlags.NonPublic, [typeof(IEnumerable), typeof(Func<TElement, int>)])!;

    private static readonly MethodInfo SnapshotMethod = typeof(AirtableListOfReferenceTypesComparer<TConcreteList, TElement>).GetMethod(
        nameof(Snapshot), BindingFlags.Static | BindingFlags.NonPublic, [typeof(object), typeof(Func<TElement, TElement>)])!;

    /// <summary>
    ///     Creates a new instance of the list comparer.
    /// </summary>
    /// <param name="elementComparer">The comparer to use for comparing elements.</param>
    public AirtableListOfReferenceTypesComparer(ValueComparer elementComparer)
        : base(
            CompareLambda(elementComparer),
            GetHashCodeLambda(elementComparer),
            SnapshotLambda(elementComparer))
        => ElementComparer = elementComparer;

    /// <summary>
    ///     The comparer to use for comparing elements.
    /// </summary>
    public ValueComparer ElementComparer { get; }

    ValueComparer IInfrastructure<ValueComparer>.Instance
        => ElementComparer;

    private static Expression<Func<object?, object?, bool>> CompareLambda(ValueComparer elementComparer)
    {
        var prm1 = Expression.Parameter(typeof(object), "a");
        var prm2 = Expression.Parameter(typeof(object), "b");

        return Expression.Lambda<Func<object?, object?, bool>>(
            Expression.Call(
                CompareMethod,
                prm1,
                prm2,
                elementComparer.EqualsExpression),
            prm1,
            prm2);
    }

    private static Expression<Func<object, int>> GetHashCodeLambda(ValueComparer elementComparer)
    {
        var prm = Expression.Parameter(typeof(object), "o");

        return Expression.Lambda<Func<object, int>>(
            Expression.Call(
                GetHashCodeMethod,
                Expression.Convert(
                    prm,
                    typeof(IEnumerable)),
                elementComparer.HashCodeExpression),
            prm);
    }

    private static Expression<Func<object, object>> SnapshotLambda(ValueComparer elementComparer)
    {
        var prm = Expression.Parameter(typeof(object), "source");

        return Expression.Lambda<Func<object, object>>(
            Expression.Call(
                SnapshotMethod,
                prm,
                elementComparer.SnapshotExpression),
            prm);
    }

    private static bool Compare(object? a, object? b, Func<TElement?, TElement?, bool> elementCompare)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null)
        {
            return b is null;
        }

        if (b is null)
        {
            return false;
        }

        if (a is IList<TElement?> aList && b is IList<TElement?> bList)
        {
            if (aList.Count != bList.Count)
            {
                return false;
            }

            for (var i = 0; i < aList.Count; i++)
            {
                var (el1, el2) = (aList[i], bList[i]);
                if (el1 is null)
                {
                    if (el2 is null)
                    {
                        continue;
                    }

                    return false;
                }

                if (el2 is null)
                {
                    return false;
                }

                if (!elementCompare(el1, el2))
                {
                    return false;
                }
            }

            return true;
        }

        throw new InvalidOperationException(
            CoreStrings.BadListType(
                (a is IList<TElement?> ? b : a).GetType().ShortDisplayName(),
                typeof(IList<>).MakeGenericType(typeof(TElement)).ShortDisplayName()));
    }

    private static int GetHashCode(IEnumerable source, Func<TElement?, int> elementGetHashCode)
    {
        var hash = new HashCode();

        foreach (var el in source)
        {
            hash.Add(el == null ? 0 : elementGetHashCode((TElement?)el));
        }

        return hash.ToHashCode();
    }

    private static IList<TElement?> Snapshot(object source, Func<TElement?, TElement?> elementSnapshot)
    {
        if (source is not IList<TElement?> sourceList)
        {
            throw new InvalidOperationException(
                CoreStrings.BadListType(
                    source.GetType().ShortDisplayName(),
                    typeof(IList<>).MakeGenericType(typeof(TElement)).ShortDisplayName()));
        }

        if (IsArray)
        {
            var snapshot = new TElement?[sourceList.Count];
            for (var i = 0; i < sourceList.Count; i++)
            {
                var instance = sourceList[i];
                snapshot[i] = instance == null ? null : elementSnapshot(instance);
            }

            return snapshot;
        }
        else
        {
            var snapshot = IsReadOnly ? new List<TElement?>() : (IList<TElement?>)Activator.CreateInstance<TConcreteList>()!;
            foreach (var e in sourceList)
            {
                snapshot.Add(e == null ? null : elementSnapshot(e));
            }

            return IsReadOnly
                ? (IList<TElement?>)Activator.CreateInstance(typeof(TConcreteList), snapshot)!
                : snapshot;
        }
    }
}
