// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if NETSTANDARD2_0 || NETSTANDARD2_1 || NETCOREAPP2_0 || NETCOREAPP2_1 || NETCOREAPP2_2 || NETCOREAPP3_0 || NETCOREAPP3_1 || NET45 || NET451 || NET452 || NET6 || NET461 || NET462 || NET47 || NET471 || NET472 || NET48

using System.ComponentModel;

namespace System.Runtime.CompilerServices
{
	/// <summary>
	/// Reserved to be used by the compiler for tracking metadata.
	/// This class should not be used by developers in source code.
	/// </summary>
	[EditorBrowsable(EditorBrowsableState.Never)]
	internal static class IsExternalInit
	{
	}

	/// <summary>
	/// Indicates that compiler support for a particular feature is required for the location where this attribute is applied.
	/// </summary>
	[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
	internal sealed class CompilerFeatureRequiredAttribute : Attribute
	{
		public CompilerFeatureRequiredAttribute(string featureName)
		{
			FeatureName = featureName;
		}

		/// <summary>
		/// The name of the compiler feature.
		/// </summary>
		public string FeatureName { get; }

		/// <summary>
		/// If true, the compiler can choose to allow access to the location where this attribute is applied if it does not understand <see cref="FeatureName"/>.
		/// </summary>
		public bool IsOptional { get; init; }

		/// <summary>
		/// The <see cref="FeatureName"/> used for the ref structs C# feature.
		/// </summary>
		public const string RefStructs = nameof(RefStructs);

		/// <summary>
		/// The <see cref="FeatureName"/> used for the required members C# feature.
		/// </summary>
		public const string RequiredMembers = nameof(RequiredMembers);
	}

	/// <summary>Specifies that a type has required members or that a member is required.</summary>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
	internal sealed class RequiredMemberAttribute : Attribute
	{
	}
}

namespace System.Diagnostics.CodeAnalysis
{
	/// <summary>
	/// Specifies that this constructor sets all required members for the current type, and callers
	/// do not need to set any required members themselves.
	/// </summary>
	internal sealed class SetsRequiredMembersAttribute : Attribute
	{
	}
}

#endif
