﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace eazdevirt.Util
{
	public static class Extensions
	{
		/// <summary>
		/// Check if the method's body contains the given pattern.
		/// </summary>
		/// <param name="method">Method to check</param>
		/// <param name="codePattern">Pattern to check for</param>
		/// <returns>true if match, false if not</returns>
		public static Boolean Matches(this MethodDef method, params Code[] codePattern)
		{
			return Matches(method, new List<Code>(codePattern));
		}

		/// <summary>
		/// Check if the method's body contains the given pattern.
		/// </summary>
		/// <param name="method">Method to check</param>
		/// <param name="codePattern">Pattern to check for</param>
		/// <returns>true if match, false if not</returns>
		public static Boolean Matches(this MethodDef method, IList<Code> codePattern)
		{
			if (method == null || codePattern == null)
				throw new ArgumentNullException();

			if (!method.HasBody || !method.Body.HasInstructions)
				return false;

			return (Helpers.FindOpCodePatterns(method.Body.Instructions, codePattern).Count > 0);
		}

		/// <summary>
		/// Check if the method body entirely matches the given pattern.
		/// </summary>
		/// <param name="method">Method to check</param>
		/// <param name="codePattern">Pattern to check against</param>
		/// <returns>true if match, false if not</returns>
		public static Boolean MatchesEntire(this MethodDef method, params Code[] codePattern)
		{
			return MatchesEntire(method, new List<Code>(codePattern));
		}

		/// <summary>
		/// Check if the method body entirely matches the given pattern.
		/// </summary>
		/// <param name="method">Method to check</param>
		/// <param name="codePattern">Pattern to check against</param>
		/// <returns>true if match, false if not</returns>
		public static Boolean MatchesEntire(this MethodDef method, IList<Code> codePattern)
		{
			if (method == null)
				throw new ArgumentNullException();

			if (!method.HasBody || !method.Body.HasInstructions)
				return false;

			var instructions = Helpers.FindOpCodePatterns(method.Body.Instructions, codePattern);
			return (instructions.Count == 1 && instructions[0].Length == method.Body.Instructions.Count);
		}

		/// <summary>
		/// Check if the method calls another method which matches the given pattern.
		/// </summary>
		/// <param name="method">Method to check</param>
		/// <param name="codePattern">Pattern to check against</param>
		/// <returns>true if method calls another which matches, false if not</returns>
		public static Boolean MatchesIndirect(this MethodDef method, params Code[] codePattern)
		{
			return MatchesIndirect(method, new List<Code>(codePattern));
		}

		/// <summary>
		/// Check if the method calls another method which matches the given pattern.
		/// </summary>
		/// <param name="method">Method to check</param>
		/// <param name="codePattern">Pattern to check against</param>
		/// <returns>true if method calls another which matches, false if not</returns>
		public static Boolean MatchesIndirect(this MethodDef method, IList<Code> codePattern)
		{
			if (method == null)
				throw new ArgumentNullException();

			return method.Calls().FirstOrDefault((called) =>
			{
				MethodDef def = called as MethodDef;
				if (def == null)
					return false;
				else return def.Matches(codePattern);
			}) != null;
		}

		/// <summary>
		/// Search for and return all called methods.
		/// </summary>
		/// <param name="method">Method to search</param>
		/// <returns>Called methods</returns>
		public static IEnumerable<IMethod> Calls(this MethodDef method)
		{
			if (method == null)
				throw new ArgumentNullException();

			return DotNetUtils.GetMethodCalls(method);
		}

		/// <summary>
		/// Find the first occurrence of an opcode pattern, returning the matching instruction sequence.
		/// </summary>
		/// <param name="method">Method to search</param>
		/// <param name="pattern">Pattern to search for</param>
		/// <returns>Matching instruction sequence, or null if none found</returns>
		public static IList<Instruction> Find(this MethodDef method, params Code[] pattern)
		{
			return Find(method, new List<Code>(pattern));
		}

		/// <summary>
		/// Find the first occurrence of an opcode pattern, returning the matching instruction sequence.
		/// </summary>
		/// <param name="method">Method to search</param>
		/// <param name="pattern">Pattern to search for</param>
		/// <returns>Matching instruction sequence, or null if none found</returns>
		public static IList<Instruction> Find(this MethodDef method, IList<Code> pattern)
		{
			if (method == null)
				throw new ArgumentNullException();

			var result = Helpers.FindOpCodePatterns(method.Body.Instructions, pattern);
			if (result.Count == 0)
				return null;
			else return result[0];
		}

		/// <summary>
		/// Find all occurrences of an opcode pattern in a method.
		/// </summary>
		/// <param name="method">Method to search</param>
		/// <param name="pattern">Pattern to search for</param>
		/// <returns>All matching instruction sequences</returns>
		public static IList<Instruction[]> FindAll(this MethodDef method, params Code[] pattern)
		{
			return FindAll(method, new List<Code>(pattern));
		}

		/// <summary>
		/// Find all occurrences of an opcode pattern in a method.
		/// </summary>
		/// <param name="method">Method to search</param>
		/// <param name="pattern">Pattern to search for</param>
		/// <returns>All matching instruction sequences</returns>
		public static IList<Instruction[]> FindAll(this MethodDef method, IList<Code> pattern)
		{
			if (method == null)
				throw new ArgumentNullException();

			var result = Helpers.FindOpCodePatterns(method.Body.Instructions, pattern);
			return result;
		}

        /// <summary>
        /// Reads a jumbled int, which is used for opcodes.
        /// </summary>
        /// <param name="reader">BinaryReader to read with</param>
        /// <returns>A proper Int32</returns>
	    public static Int32 ReadInt32Special(this BinaryReader reader)
        {
            byte[] b = reader.ReadBytes(4);
            //	return (int)this.\u0003[0] << 24 | (int)this.\u0003[1] << 16 | (int)this.\u0003[2] | (int)this.\u0003[3] << 8;
            return b[0] << 24 | b[1] << 16 | b[2] | b[3] << 8;
        }
	}
}
