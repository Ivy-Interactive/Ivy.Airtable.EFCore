// This class is derived in large part from `JsonCollectionOfReferencesReaderWriter`:
// https://github.com/dotnet/efcore/blob/30eb1fbb88f6bc7fe31217f83933122a2a403e59/src/EFCore/Storage/Json/JsonCollectionOfReferencesReaderWriter.cs
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

using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace Airtable.EFCore.Storage.Internal;

#pragma warning disable EF1001 // Internal EF Core API usage.

public class AirtableJsonCollectionOfReferencesReaderWriter<TConcreteCollection, TElement> :
    JsonValueReaderWriter<object>,
    ICompositeJsonValueReaderWriter
    where TElement : class?
{
    private readonly JsonValueReaderWriter _elementReaderWriter;

    private static readonly bool IsArray = typeof(TConcreteCollection).IsArray;

    private static readonly bool IsReadOnly = IsArray
        || (typeof(TConcreteCollection).IsGenericType
            && typeof(TConcreteCollection).GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>));

    /// <summary>
    ///     Creates a new instance of this collection reader/writer, using the given reader/writer for its elements.
    /// </summary>
    /// <param name="elementReaderWriter">The reader/writer to use for each element.</param>
    public AirtableJsonCollectionOfReferencesReaderWriter(JsonValueReaderWriter elementReaderWriter)
        => _elementReaderWriter = elementReaderWriter;

    /// <inheritdoc />
    public override object FromJsonTyped(ref Utf8JsonReaderManager manager, object? existingObject = null)
    {
        IList<TElement?> collection;
        if (IsReadOnly)
        {
            collection = new List<TElement?>();
        }
        else if (existingObject == null)
        {
            collection = (IList<TElement?>)Activator.CreateInstance<TConcreteCollection>()!;
        }
        else
        {
            collection = (IList<TElement?>)existingObject;
            collection.Clear();
        }

        var tokenType = manager.CurrentReader.TokenType;
        if (tokenType != JsonTokenType.StartArray)
        {
            throw new InvalidOperationException(
                CoreStrings.JsonReaderInvalidTokenType(tokenType.ToString()));
        }

        while (tokenType != JsonTokenType.EndArray)
        {
            manager.MoveNext();
            tokenType = manager.CurrentReader.TokenType;

            switch (tokenType)
            {
                case JsonTokenType.String:
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.StartArray:
                case JsonTokenType.StartObject:
                    collection.Add((TElement)_elementReaderWriter.FromJson(ref manager));
                    break;
                case JsonTokenType.Null:
                    collection.Add(default);
                    break;
                case JsonTokenType.Comment:
                case JsonTokenType.EndArray:
                case JsonTokenType.EndObject:
                    break;
                case JsonTokenType.None: // Explicitly listing all states that we throw for
                case JsonTokenType.PropertyName:
                default:
                    throw new InvalidOperationException(
                        CoreStrings.JsonReaderInvalidTokenType(tokenType.ToString()));
            }
        }

        return IsReadOnly
            ? IsArray
                ? collection.ToArray()
                : (IList<TElement?>)Activator.CreateInstance(typeof(TConcreteCollection), collection)!
            : collection;
    }

    /// <inheritdoc />
    public override void ToJsonTyped(Utf8JsonWriter writer, object? value)
    {
        writer.WriteStartArray();
        if (value != null)
        {
            foreach (var element in (IEnumerable<object?>)value)
            {
                if (element == null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    _elementReaderWriter.ToJson(writer, element);
                }
            }
        }

        writer.WriteEndArray();
    }

    JsonValueReaderWriter ICompositeJsonValueReaderWriter.InnerReaderWriter
        => _elementReaderWriter;
}
