// This class is derived in large part from `SqliteConnectionStringBuilder`:
// https://github.com/dotnet/efcore/blob/30eb1fbb88f6bc7fe31217f83933122a2a403e59/src/Microsoft.Data.Sqlite.Core/SqliteConnectionStringBuilder.cs
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
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;

namespace Airtable.EFCore;

public class AirtableDatabaseConnectionStringBuilder : DbConnectionStringBuilder
{
    private const string BaseIdKeyword = "BaseId";
    private const string ApiKeyKeyword = "ApiKey";

    private enum Keywords
    {
        BaseId,
        ApiKey,
    }

    private static readonly IReadOnlyList<string> _validKeywords;
    private static readonly IReadOnlyDictionary<string, Keywords> _keywords;

    private string _baseId = string.Empty;
    private string _apiKey = string.Empty;

    static AirtableDatabaseConnectionStringBuilder()
    {
        var validKeywords = new string[2];
        validKeywords[(int)Keywords.BaseId] = BaseIdKeyword;
        validKeywords[(int)Keywords.ApiKey] = ApiKeyKeyword;
        _validKeywords = validKeywords;

        _keywords = new Dictionary<string, Keywords>(StringComparer.OrdinalIgnoreCase)
        {
            [BaseIdKeyword] = Keywords.BaseId,
            [ApiKeyKeyword] = Keywords.ApiKey,
        };
    }

    public AirtableDatabaseConnectionStringBuilder()
    {
    }

    public AirtableDatabaseConnectionStringBuilder(string? connectionString)
    {
        ConnectionString = connectionString;
    }


    public virtual string BaseId
    {
        get => _baseId;
        set
        {
            _baseId = value ?? string.Empty;
            base[BaseIdKeyword] = _baseId;
        }
    }

    public virtual string ApiKey
    {
        get => _apiKey;
        set
        {
            _apiKey = value ?? string.Empty;
            base[ApiKeyKeyword] = _apiKey;
        }
    }

    public override ICollection Keys
        => new ReadOnlyCollection<string>((string[])_validKeywords);

    public override ICollection Values
    {
        get
        {
            var values = _validKeywords.Select((_, i) => GetAt((Keywords)i)).ToArray();
            return new ReadOnlyCollection<object?>(values);
        }
    }

    public override object? this[string keyword]
    {
#pragma warning disable CS8764
        get => GetAt(GetIndex(keyword));
#pragma warning restore CS8764
        set
        {
            if (value == null)
            {
                Remove(keyword);
                return;
            }

            switch (GetIndex(keyword))
            {
                case Keywords.BaseId:
#pragma warning disable CS8601
                    BaseId = Convert.ToString(value, CultureInfo.InvariantCulture);
#pragma warning restore CS8601

                    return;

                case Keywords.ApiKey:
#pragma warning disable CS8601
                    ApiKey = Convert.ToString(value, CultureInfo.InvariantCulture);
#pragma warning restore CS8601

                    return;

                default:
                    Debug.Fail("Unexpected keyword: " + keyword);
                    return;
            }
        }
    }

    public override void Clear()
    {
        base.Clear();

        for (var i = 0; i < _validKeywords.Count; i++)
        {
            Reset((Keywords)i);
        }
    }

    public override bool ContainsKey(string keyword)
        => _keywords.ContainsKey(keyword);

    public override bool Remove(string keyword)
    {
        if (!_keywords.TryGetValue(keyword, out var index)
            || !base.Remove(_validKeywords[(int)index]))
        {
            return false;
        }

        Reset(index);

        return true;
    }

    public override bool ShouldSerialize(string keyword)
        => _keywords.TryGetValue(keyword, out var index) && base.ShouldSerialize(_validKeywords[(int)index]);

#pragma warning disable CS8765
    public override bool TryGetValue(string keyword, out object? value)
#pragma warning restore CS8765
    {
        if (!_keywords.TryGetValue(keyword, out var index))
        {
            value = null;

            return false;
        }

        value = GetAt(index);

        return true;
    }

    private object? GetAt(Keywords index)
    {
        switch (index)
        {
            case Keywords.BaseId:
                return BaseId;

            case Keywords.ApiKey:
                return ApiKey;

            default:
                Debug.Fail("Unexpected keyword: " + index);
                return null;
        }
    }

    private static Keywords GetIndex(string keyword)
        => !_keywords.TryGetValue(keyword, out var index)
            ? throw new ArgumentException($"Connection string keyword '{keyword}' is not supported.")
            : index;

    private void Reset(Keywords index)
    {
        switch (index)
        {
            case Keywords.BaseId:
                _baseId = string.Empty;
                return;

            case Keywords.ApiKey:
                _apiKey = string.Empty;
                return;

            default:
                Debug.Fail("Unexpected keyword: " + index);
                return;
        }
    }
}
