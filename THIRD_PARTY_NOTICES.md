# Third-Party Notices

This project includes or references third-party software and third-party materials that remain under their own respective licenses.

## V8

This project is inspired by the V8 JavaScript Engine.
Portions of the design are influenced by V8.

V8 is a separate project licensed under the BSD 3-Clause License.

Reference:

- <https://github.com/v8/v8/blob/main/LICENSE>

The presence of this notice does not change the license of Okojo/Okojo itself. The Okojo project license is the root [LICENSE](LICENSE).

## .NET runtime

The atom string index uses the ordinal hash recurrence, reciprocal reduction,
and Marvin round studied or adapted from .NET runtime v10.0.12. The guarded
UTF-16 readers and append-only table are Okojo implementations.

Sources: [String.Comparison.cs](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Private.CoreLib/src/System/String.Comparison.cs),
[HashHelpers.cs](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Private.CoreLib/src/System/Collections/HashHelpers.cs),
[Marvin.cs](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Private.CoreLib/src/System/Marvin.cs).

Copyright (c) .NET Foundation and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
