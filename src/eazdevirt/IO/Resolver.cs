﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.MD;
using eazdevirt.Util;

namespace eazdevirt.IO
{
	public partial class Resolver : ResourceReader
	{
		/// <summary>
		/// Logger.
		/// </summary>
		public ILogger Logger { get; private set; }

		/// <summary>
		/// Importer.
		/// </summary>
		public Importer Importer { get; private set; }

		/// <summary>
		/// Serialization version. Determines how certain things are read.
		/// </summary>
		public SerializationVersion Version { get; private set; }

		/// <summary>
		/// Lock used for all public resolve methods.
		/// </summary>
		private Object _lock = new Object();

		public Resolver(EazModule module, ILogger logger,
			SerializationVersion version = SerializationVersion.V1)
			: base(module)
		{
			this.Logger = (logger != null ? logger : DummyLogger.NoThrowInstance);
			this.Importer = new Importer(this.Module, ImporterOptions.TryToUseDefs);
			this.Version = version;
		}

		/// <summary>
		/// Resolve a user string.
		/// </summary>
		/// <param name="position">Position</param>
		/// <returns>String</returns>
		public String ResolveString(Int32 position)
		{
			lock (_lock)
			{
				return this.ResolveString_NoLock(position);
			}
		}

		String ResolveString_NoLock(Int32 position)
		{
			this.Stream.Position = position;

			InlineOperand operand = new InlineOperand(this.Reader);
			if (operand.IsToken)
			{
				return this.Module.ReadUserString((UInt32)operand.Token);
			}
			else
			{
				StringData data = operand.Data as StringData;
				return data.Value;
			}
		}

		/// <summary>
		/// Resolve a method.
		/// </summary>
		/// <param name="position">Position</param>
		/// <returns>IMethod</returns>
		public IMethod ResolveMethod(Int32 position)
		{
			lock(_lock)
			{
				return this.ResolveMethod_NoLock(position);
			}
		}

		/// <summary>
		/// Try to resolve a method without throwing.
		/// </summary>
		/// <param name="position">Position</param>
		/// <returns>IMethod, or null if unable to resolve</returns>
		public IMethod TryResolveMethod(Int32 position)
		{
			try
			{
				return this.ResolveMethod(position);
			}
			catch(Exception)
			{
				return null;
			}
		}

		IMethod ResolveMethod_Helper(ITypeDefOrRef declaringType, MethodData data)
		{
			TypeDef declaringDef = declaringType.ResolveTypeDefThrow();
			MethodSig methodSig = GetMethodSig(data);

			// Has a GenericMVar
			if (data.HasGenericArguments)
			{
				MethodSig detectedSig = null;
				MethodDef method = FindMethodCheckBaseType(declaringType, data, out detectedSig);

				if (method == null || detectedSig == null)
				{
					throw new Exception(String.Format(
						"Unable to find generic method from the declaring/base types: DeclaringType={0}, MethodName={1}, MethodSig={2}",
						declaringType.ReflectionFullName, data.Name, methodSig));
				}

				MethodSpec methodSpec = new MethodSpecUser(method, ToGenericInstMethodSig(data));
				return this.Importer.Import(methodSpec);
			}
			else // No GenericMVars
			{
				MethodDef method = declaringDef.FindMethodCheckBaseType(data.Name, methodSig);
				if (method == null)
				{
					throw new Exception(String.Format(
						"Unable to find method from the declaring/base types: DeclaringType={0}, MethodName={1}, MethodSig={2}",
						declaringType.ReflectionFullName, data.Name, methodSig));
				}

				return this.Importer.Import(method);
			}
		}

		IMethod ResolveMethod_NoLock(TypeDef declaringDef, MethodData data)
		{
			return ResolveMethod_Helper(declaringDef, data);
		}

		/// <summary>
		/// Find a MethodDef from a declaring type and some MethodData. Will generate
		/// a list of possible MethodSigs and check against each of them, returning the
		/// first-found MethodDef that matches the method name and signature.
		/// </summary>
		/// <param name="declaringType">Declaring type</param>
		/// <param name="data">MethodData</param>
		/// <param name="detectedSig">The detected MethodSig</param>
		/// <returns>MethodDef if found, null if none found</returns>
		MethodDef FindMethodCheckBaseType(ITypeDefOrRef declaringType, MethodData data, out MethodSig detectedSig)
		{
			detectedSig = null;

			TypeDef declaringDef = declaringType.ResolveTypeDef();
			if (declaringDef == null)
				return null;

			MethodDef method = null;
			MethodSig methodSig = GetMethodSig(data);
			var possibleSigs = PossibleMethodSigs(declaringType, methodSig, data);
			detectedSig = possibleSigs.FirstOrDefault(sig => {
				return (method = declaringDef.FindMethodCheckBaseType(data.Name, sig)) != null;
			});

			return method;
		}

		/// <summary>
		/// Get a GenericInstMethodSig containing the resolved types of the generic method vars
		/// specified in the given MethodData.
		/// </summary>
		/// <param name="data">MethodData</param>
		/// <returns>GenericInstMethodSig, or null if the given data contains no generic arguments</returns>
		GenericInstMethodSig ToGenericInstMethodSig(MethodData data)
		{
			if (!data.HasGenericArguments)
				return null;

			IList<TypeSig> genericMVars = new List<TypeSig>();
			for (Int32 i = 0; i < data.GenericArguments.Length; i++)
				genericMVars.Add(this.ResolveType_NoLock(data.GenericArguments[i].Position).ToTypeSig());

			return new GenericInstMethodSig(genericMVars);
		}

		IMethod ResolveMethod_NoLock(TypeRef declaringRef, MethodData data)
		{
			return ResolveMethod_Helper(declaringRef, data);
		}

		IMethod ResolveMethod_NoLock(TypeSpec declaringSpec, MethodData data)
		{
			// Find a method that matches the signature (factoring in possible generic vars/mvars)
			MethodSig matchedSig = null;
			MethodDef method = FindMethodCheckBaseType(declaringSpec, data, out matchedSig);

			if (matchedSig == null || method == null)
			{
				throw new Exception(String.Format(
					"Unable to find generic method from the declaring/base types: DeclaringType={0}, MethodName={1}",
					declaringSpec.ReflectionFullName, data.Name));
			}

			MemberRef memberRef = new MemberRefUser(this.Module, method.Name, matchedSig, declaringSpec);
			if (data.HasGenericArguments)
				return this.Importer.Import(new MethodSpecUser(memberRef, ToGenericInstMethodSig(data)));
			else
				return this.Importer.Import(memberRef);
		}

		IMethod ResolveMethod_NoLock(Int32 position)
		{
			this.Stream.Position = position;

			InlineOperand operand = new InlineOperand(this.Reader);
			if(operand.IsToken)
			{
				MDToken token = new MDToken(operand.Token);
				if (token.Table == Table.Method)
					return this.Module.ResolveMethod(token.Rid);
				else if (token.Table == Table.MemberRef)
					return this.Module.ResolveMemberRef(token.Rid);
				else if (token.Table == Table.MethodSpec)
					return this.Module.ResolveMethodSpec(token.Rid);

				throw new Exception("[ResolveMethod_NoLock] Bad MDToken table");
			}
			else
			{
				MethodData data = operand.Data as MethodData;
				ITypeDefOrRef declaring = this.ResolveType_NoLock(data.DeclaringType.Position);

				if (declaring is TypeDef)
				{
					TypeDef declaringDef = declaring as TypeDef;
					return this.ResolveMethod_NoLock(declaringDef, data);
				}
				else if (declaring is TypeRef)
				{
					TypeRef declaringRef = declaring as TypeRef;
					return this.ResolveMethod_NoLock(declaringRef, data);
				}
				else if (declaring is TypeSpec)
				{
					TypeSpec declaringSpec = declaring as TypeSpec;
					return this.ResolveMethod_NoLock(declaringSpec, data);
				}

				throw new Exception("[ResolveMethod_NoLock] Expected declaring type to be a TypeDef, TypeRef or TypeSpec");
			}
		}

		/// <summary>
		/// Resolve a field.
		/// </summary>
		/// <param name="position">Position</param>
		/// <returns>Field</returns>
		public IField ResolveField(Int32 position)
		{
			lock (_lock)
			{
				return this.ResolveField_NoLock(position);
			}
		}

		public IField TryResolveField(Int32 position)
		{
			try
			{
				return this.ResolveField(position);
			}
			catch (Exception)
			{
				return null;
			}
		}

		IField ResolveField_NoLock(Int32 position)
		{
			this.Stream.Position = position;

			InlineOperand operand = new InlineOperand(this.Reader);
			if (operand.IsToken)
			{
				MDToken token = new MDToken(operand.Token);
				return this.Module.ResolveField(token.Rid);
			}
			else
			{
				FieldData data = operand.Data as FieldData;
				ITypeDefOrRef declaringType = this.ResolveType_NoLock(data.FieldType.Position);
				if (declaringType == null)
					throw new Exception("[ResolveField_NoLock] Unable to resolve type as TypeDef or TypeRef");

				NameResolver nameResolver = new NameResolver(this.Module);
				IField field = nameResolver.ResolveField(declaringType, data.Name);
				if (field == null)
				{
					throw new Exception(String.Format(
					"[ResolveField_NoLock] Unable to resolve field: DeclaringType={0}, Field={1}",
					declaringType.ReflectionFullName, data.Name));
				}

				return field;
			}
		}

		/// <summary>
		/// Resolve a type.
		/// </summary>
		/// <param name="position">Position</param>
		/// <returns>Type</returns>
		public ITypeDefOrRef ResolveType(Int32 position)
		{
			lock (_lock)
			{
				return this.ResolveType_NoLock(position);
			}
		}

		public ITypeDefOrRef TryResolveType(Int32 position)
		{
			try
			{
				return this.ResolveType(position);
			}
			catch (Exception)
			{
				return null;
			}
		}

		ITypeDefOrRef ResolveType_NoLock(Int32 position)
		{
			this.Stream.Position = position;

			InlineOperand operand = new InlineOperand(this.Reader);
			if (operand.IsToken)
			{
				MDToken token = new MDToken(operand.Token);

				if (token.Table == Table.TypeDef)
					return this.Module.ResolveTypeDef(token.Rid);
				else if (token.Table == Table.TypeRef)
					return this.Module.ResolveTypeRef(token.Rid);
				else if (token.Table == Table.TypeSpec)
					return this.Module.ResolveTypeSpec(token.Rid);

				throw new Exception("Unable to resolve type: bad MDToken table");
			}
			else
			{
				TypeData data = operand.Data as TypeData;

				// Resolve via name
				TypeName typeName = new TypeName(data.Name);
				NameResolver nameResolver = new NameResolver(this.Module);
				ITypeDefOrRef typeDefOrRef = nameResolver.ResolveTypeDefOrRef(typeName);

				if (typeDefOrRef == null)
				{
					throw new Exception(String.Format(
						"Unable to resolve ITypeDefOrRef from given name: {0}",
						typeName.FullName));
				}

				// Apply generics, if any (resulting in a TypeSpec)
				if (data.GenericTypes.Length > 0)
					typeDefOrRef = ApplyGenerics(typeDefOrRef, data);

				if (typeDefOrRef == null)
				{
					throw new Exception(String.Format(
						"Unable to apply generic types: {0}", typeName.FullName
						));
				}

				// Apply [], *, &
				typeDefOrRef = SigUtil.FromBaseSig(typeDefOrRef.ToTypeSig(), typeName.Modifiers)
					.ToTypeDefOrRef();

				return typeDefOrRef;
			}
		}

		/// <summary>
		/// Resolve a token.
		/// </summary>
		/// <param name="position">Position</param>
		/// <returns>Token</returns>
		public ITokenOperand ResolveToken(Int32 position)
		{
			lock (_lock)
			{
				return this.ResolveToken_NoLock(position);
			}
		}

		public ITokenOperand TryResolveToken(Int32 position)
		{
			try
			{
				return this.ResolveToken(position);
			}
			catch (Exception)
			{
				return null;
			}
		}

		ITokenOperand ResolveToken_NoLock(Int32 position)
		{
			this.Stream.Position = position;

			InlineOperand operand = new InlineOperand(this.Reader);
			if (operand.IsToken)
			{
				throw new NotSupportedException("Currently unable to resolve a token via MDToken");
			}
			else
			{
				if (operand.Data.Type == InlineOperandType.Field)
					return this.ResolveField_NoLock(position);
				if (operand.Data.Type == InlineOperandType.Method)
					return this.ResolveMethod_NoLock(position);
				if (operand.Data.Type == InlineOperandType.Type)
					return this.ResolveType_NoLock(position);

				throw new InvalidOperationException(String.Format(
					"Expected inline operand type of token to be either Type, Field or Method; instead got {0}",
					operand.Data.Type));
			}
		}

		public IMethod ResolveEazCall(Int32 value)
		{
			lock (_lock)
			{
				return this.ResolveEazCall_NoLock(value);
			}
		}

		IMethod ResolveEazCall_NoLock(Int32 value)
		{
			// Currently unsure what these two flags do
			Boolean flag1 = (value & 0x80000000) != 0; // Probably indicates the method has no generic vars
			Boolean flag2 = (value & 0x40000000) != 0;
			Int32 position = value & 0x3FFFFFFF; // Always a stream position

			if (flag1)
			{
				//throw new Exception(String.Format(
				//	"Unsure what to do if EazCall operand flag1 is set (operand = 0x{0:X8})",
				//	value
				//));

				// void DoSomething(Int32, Type[], Type[], Boolean):
				// -> DoSomething(position, null, null, flag2);

				return ResolveEazCall_Helper(position, null, null, flag1);
			}
			else
			{
				this.Stream.Position = position;

				InlineOperand operand = new InlineOperand(this.Reader);
				UnknownType7 data = operand.Data as UnknownType7;

				Int32 num = data.Unknown1;
				// This method is used to get generics info?
				IMethod method = this.ResolveMethod_NoLock(data.Unknown2);
				Type[] genericTypes = null; // Todo
				Type[] declaringGenericTypes = null; // Todo

				Boolean subflag1 = (num & 0x40000000) != 0;
				num &= unchecked((Int32)0xBFFFFFFF);

				// -> DoSomething(num, genericTypes, declaringGenericTypes, subflag1);
				return ResolveEazCall_Helper(num, genericTypes, declaringGenericTypes, subflag1);
			}
		}

		IMethod ResolveEazCall_Helper(
			Int32 value, Type[] genericTypes, Type[] declaringGenericTypes, Boolean flag)
		{
			this.Stream.Position = value;

			if (this.Version == SerializationVersion.V1)
			{
				if (this.Reader.ReadByte() != 0)
					throw new InvalidDataException();
			}

			EazCallData data = new EazCallData(this.Reader);

			var delcaringType = (this.ResolveType_NoLock(data.MethodInfo.DeclaringType) as TypeDef);
			if (delcaringType == null)
				throw new Exception("Unable to resolve the declaring type of the Eaz_Call method operand");

			var methodSig = GetMethodSig(data);

			// Todo: Factor in generics?
			var method = delcaringType.FindMethodCheckBaseType(data.MethodInfo.Name, methodSig);
			if (method == null)
				throw new Exception("Unable to resolve Eaz_Call operand from declaring type + method name");

			return method;
		}

		/// <summary>
		/// Resolve a type from a deserialized inline operand, which should
		/// have a direct token (position).
		/// </summary>
		/// <param name="operand">Inline operand</param>
		/// <returns>TypeSig</returns>
		TypeSig ResolveType(InlineOperand operand)
		{
			ITypeDefOrRef type = this.ResolveType_NoLock(operand.Position);
			return type.ToTypeSig(true);
		}

		TypeSpec ApplyGenerics(ITypeDefOrRef type, IList<TypeSig> generics)
		{
			ClassOrValueTypeSig typeSig = type.ToTypeSig().ToClassOrValueTypeSig();
			GenericInstSig genericSig = new GenericInstSig(typeSig, generics);
			return new TypeSpecUser(genericSig);
		}

		/// <summary>
		/// Create a TypeSpec from a type and data with generic arguments.
		/// from deserialized data to an existing type.
		/// </summary>
		/// <param name="type">Existing type</param>
		/// <param name="data">Deserialized data with generic types</param>
		/// <returns>GenericInstSig</returns>
		TypeSpec ApplyGenerics(ITypeDefOrRef type, TypeData data)
		{
			List<TypeSig> generics = new List<TypeSig>();
			foreach (var g in data.GenericTypes)
			{
				var gtype = this.ResolveType_NoLock(g.Position);
				generics.Add(gtype.ToTypeSig());
			}

			return ApplyGenerics(type, generics);

			//List<TypeSig> types = new List<TypeSig>();
			//foreach (var g in data.GenericTypes)
			//{
			//	var gtype = this.ResolveType_NoLock(g.Position);
			//	types.Add(gtype.ToTypeSig());
			//}
			//
			//ClassOrValueTypeSig typeSig = type.ToTypeSig().ToClassOrValueTypeSig();
			//// return new GenericInstSig(typeSig, types).ToTypeDefOrRef();
			//
			//GenericInstSig genericSig = new GenericInstSig(typeSig, types);
			//TypeSpec typeSpec = new TypeSpecUser(genericSig);
			//return typeSpec;
		}

		IList<MethodSig> PossibleMethodSigs(ITypeDefOrRef declaringType, MethodSig sig, MethodData data)
		{
			// Setup generic types
			IList<TypeSig> typeGenerics = new List<TypeSig>(), methodGenerics = new List<TypeSig>();

			// Add all declaring spec generic types
			TypeSpec declaringSpec = declaringType as TypeSpec;
			if (declaringSpec != null)
			{
				var genericInstSig = declaringSpec.TryGetGenericInstSig();
				foreach (var garg in genericInstSig.GenericArguments)
					typeGenerics.Add(garg);
			}

			// Add all method generic types
			if (data.HasGenericArguments)
			{
				foreach (var operand in data.GenericArguments)
				{
					var gtype = this.ResolveType_NoLock(operand.Position);
					methodGenerics.Add(gtype.ToTypeSig());
				}
			}

			// Todo: Combinations factoring in the possibility that return type might match
			// a generic type
			TypeSig returnType = ResolveType(data.ReturnType);
			IList<TypeSig> returnTypes = GenericUtils.PossibleTypeSigs(returnType, typeGenerics, methodGenerics);

			TypeSig[] paramTypes = new TypeSig[data.Parameters.Length];
			for (Int32 i = 0; i < paramTypes.Length; i++)
			{
				paramTypes[i] = ResolveType(data.Parameters[i]);
			}

			UInt32 genericTypeCount = (UInt32)data.GenericArguments.Length;

			IList<MethodSig> signatures = new List<MethodSig>();
			var paramCombos = GenericUtils.CreateGenericParameterCombinations(paramTypes, typeGenerics, methodGenerics);

			foreach (var rType in returnTypes)
			{
				foreach (var combo in paramCombos)
				{
					var paramCombo = combo.ToArray();

					MethodSig methodSig;

					if (genericTypeCount == 0)
					{
						if (data.IsStatic)
							methodSig = MethodSig.CreateStatic(rType, paramCombo);
						else
							methodSig = MethodSig.CreateInstance(rType, paramCombo);
					}
					else
					{
						if (data.IsStatic)
							methodSig = MethodSig.CreateStaticGeneric(genericTypeCount, rType, paramCombo);
						else
							methodSig = MethodSig.CreateInstanceGeneric(genericTypeCount, rType, paramCombo);
					}

					signatures.Add(methodSig);
				}
			}

			return signatures;
		}

		/// <summary>
		/// Convert some EazCall data into a method signature.
		/// </summary>
		/// <param name="data">Data</param>
		/// <returns>Signature</returns>
		MethodSig GetMethodSig(EazCallData data)
		{
			var returnType = this.ResolveType_NoLock(data.MethodInfo.ReturnTypeCode).ToTypeSig(true);
			var paramTypes = data.MethodInfo.Parameters.Select((p) => {
				return this.ResolveType_NoLock(p.TypeCode).ToTypeSig(true);
			}).ToArray();

			// Unsure about how EazCallData stores/handles generics

			if (data.MethodInfo.IsStatic)
				return MethodSig.CreateStatic(returnType, paramTypes);
			else
			{
				paramTypes = paramTypes.Skip(1).ToArray();
				return MethodSig.CreateInstance(returnType, paramTypes);
			}
		}

		/// <summary>
		/// Convert some method data into a method signature.
		/// </summary>
		/// <param name="data">Data</param>
		/// <returns>Signature</returns>
		MethodSig GetMethodSig(MethodData data)
		{
			TypeSig returnType = ResolveType(data.ReturnType);

			TypeSig[] paramTypes = new TypeSig[data.Parameters.Length];
			for (Int32 i = 0; i < paramTypes.Length; i++)
			{
				paramTypes[i] = ResolveType(data.Parameters[i]);
			}

			UInt32 genericTypeCount = (UInt32)data.GenericArguments.Length;

			MethodSig methodSig;
			if (genericTypeCount == 0)
			{
				if (data.IsStatic)
					methodSig = MethodSig.CreateStatic(returnType, paramTypes);
				else
					methodSig = MethodSig.CreateInstance(returnType, paramTypes);
			}
			else
			{
				if (data.IsStatic)
					methodSig = MethodSig.CreateStaticGeneric(genericTypeCount, returnType, paramTypes);
				else
					methodSig = MethodSig.CreateInstanceGeneric(genericTypeCount, returnType, paramTypes);
			}

			return methodSig;
		}

		/// <summary>
		/// Apply type "modifiers" to some TypeSig given a modifiers stack.
		/// </summary>
		/// <param name="typeSig">TypeSig</param>
		/// <param name="modifiers">Modifiers stack</param>
		/// <returns>TypeSig</returns>
		TypeSig ApplyTypeModifiers(TypeSig typeSig, Stack<String> modifiers)
		{
			// This might not be implemented correctly
			typeSig = this.FixTypeAsArray(typeSig, modifiers);
			typeSig = this.FixTypeAsRefOrPointer(typeSig, modifiers);
			return typeSig;
		}

		/// <summary>
		/// Apply array "modifiers" to a TypeSig.
		/// </summary>
		/// <param name="typeSig">TypeSig</param>
		/// <param name="modifiers">Modifiers stack</param>
		/// <returns>TypeSig</returns>
		TypeSig FixTypeAsArray(TypeSig typeSig, Stack<String> modifiers)
		{
			UInt32 rank = 0;

			while(modifiers.Count > 0 && modifiers.Peek().Equals("[]"))
			{
				modifiers.Pop();
				rank++;
			}

			if (rank == 0)
				return typeSig;
			else if (rank == 1)
				return new SZArraySig(typeSig);
			else // Might want to wrap in N SZArraySigs instead of ArraySig(type, N)?
				return new ArraySig(typeSig, rank);
		}

		/// <summary>
		/// Apply ByRef or Ptr "modifiers" to a TypeSig depending on the given modifiers
		/// stack.
		/// </summary>
		/// <param name="typeSig">TypeSig</param>
		/// <param name="modifiers">Modifiers stack</param>
		/// <returns>TypeSig</returns>
		TypeSig FixTypeAsRefOrPointer(TypeSig typeSig, Stack<String> modifiers)
		{
			while(modifiers.Count > 0
				&& (modifiers.Peek().Equals("*") || modifiers.Peek().Equals("&")))
			{
				if(modifiers.Pop().Equals("*")) // Pointer
				{
					typeSig = new PtrSig(typeSig);
				}
				else // ByRef
				{
					typeSig = new ByRefSig(typeSig);
				}
			}

			return typeSig;
		}
	}
}
