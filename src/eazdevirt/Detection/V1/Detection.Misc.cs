﻿using System;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using eazdevirt.Reflection;
using eazdevirt.Util;

namespace eazdevirt.Detection.V1.Ext
{
	public static partial class Extensions
	{
		[Detect(Code.Box)]
		public static Boolean Is_Box(this VirtualOpCode ins)
		{
			return ins.Matches(new Code[] {
				Code.Ldarg_1, Code.Castclass, Code.Callvirt, Code.Stloc_0, Code.Ldarg_0, Code.Ldloc_0,
				Code.Ldc_I4_1, Code.Call, Code.Stloc_1, Code.Ldarg_0, Code.Call, Code.Callvirt, Code.Ldloc_1, Code.Call
			}) && ins.DelegateMethod.Calls().Count() == 7
			&& ins.DelegateMethod.Calls().ToArray()[1].ResolveMethodDef().Matches(Code.Call, Code.Endfinally, Code.Ldarg_2);
		}

		[Detect(Code.Call)]
		public static Boolean Is_Call(this VirtualOpCode ins)
		{
			return ins.MatchesEntire(new Code[] {
				Code.Ldarg_1, Code.Castclass, Code.Stloc_0, Code.Ldarg_0, Code.Ldloc_0,
				Code.Callvirt, Code.Call, Code.Stloc_1, Code.Ldarg_0, Code.Ldloc_1,
				Code.Ldc_I4_0, Code.Call, Code.Ret
			});
		}

		[Detect(Code.Callvirt)]
		public static Boolean Is_Callvirt(this VirtualOpCode ins)
		{
			MethodDef method;
			var sub = ins.Find(new Code[] {
				Code.Ldarg_1, Code.Castclass, Code.Stloc_S, Code.Ldarg_0, Code.Ldloc_S,
				Code.Callvirt, Code.Call, Code.Stloc_0, Code.Ldarg_0, Code.Ldfld, Code.Brfalse_S
			});
			return sub != null
				&& (method = sub[6].Operand as MethodDef) != null
				&& method.HasReturnType && method.ReturnType.FullName.Equals("System.Reflection.MethodBase");
		}

		[Detect(Code.Castclass)]
		public static Boolean Is_Castclass(this VirtualOpCode ins)
		{
			var sub = ins.DelegateMethod.Find(
				Code.Ldloc_2, Code.Ldloc_1, Code.Call, Code.Brfalse_S, Code.Ldarg_0, Code.Ldloc_2,
				Code.Call, Code.Ret, Code.Newobj
			);
			return sub != null
				&& ((IMethod)sub[8].Operand).DeclaringType.FullName.Contains("System.InvalidCastException");
		}

        [Detect(Code.Castclass)]
        public static Boolean Is_Castclass_Codeflow(this VirtualOpCode ins)
        {
            var sub = ins.DelegateMethod.Find(
                Code.Call, Code.Brtrue_S, Code.Newobj, Code.Throw, Code.Ldarg_0, Code.Ldloc_2,
                Code.Call, Code.Ret
            );
            return sub != null
                && ((IMethod)sub[2].Operand).DeclaringType.FullName.Contains("System.InvalidCastException");
        }

        private static Boolean _Is_Ceq_50(VirtualOpCode ins)
		{
			return ins.DelegateMethod.Matches(
				Code.Ldloc_2, Code.Ldloc_1, Code.Ldloc_0, Code.Call, Code.Brtrue_S,
				Code.Ldc_I4_0, Code.Br_S, Code.Ldc_I4_1
			) && ins.DelegateMethod.MatchesIndirect(
				// Helper changed in 5.0
				Code.Ceq, Code.Stloc_0, Code.Br_S, Code.Ldarg_0, Code.Castclass,
				Code.Stloc_S, Code.Ldarg_1, Code.Castclass, Code.Stloc_S,
				Code.Ldloc_S, Code.Ldloc_S, Code.Callvirt, Code.Stloc_0, Code.Ldloc_0,
				Code.Ret
			);
		}

		private static Boolean _Is_Ceq_49(VirtualOpCode ins)
		{
			return ins.DelegateMethod.Matches(
				Code.Ldloc_2, Code.Ldloc_1, Code.Ldloc_0, Code.Call, Code.Brtrue_S,
				Code.Ldc_I4_0, Code.Br_S, Code.Ldc_I4_1
			) && ins.DelegateMethod.MatchesIndirect(
				Code.Ldloc_1, Code.Callvirt, Code.Call, Code.Ldarg_1, Code.Callvirt, Code.Call,
				Code.Ceq, Code.Stloc_0, Code.Ldloc_0, Code.Ret
			);
		}

		[Detect(Code.Ceq)]
		public static Boolean Is_Ceq(this VirtualOpCode ins)
		{
			return _Is_Ceq_49(ins) || _Is_Ceq_50(ins);
		}

		/// <summary>
		/// OpCode pattern seen in the Less-Than helper method.
		/// Used in: Clt_Un, Blt, Bge_Un (negated)
		/// </summary>
		private static readonly Code[] Pattern_Clt_Un = new Code[] {
			Code.Ldloc_S, Code.Ldloc_S, Code.Blt_S,
			Code.Ldloc_S, Code.Call, Code.Brtrue_S, // System.Double::IsNaN(float64)
			Code.Ldloc_S, Code.Call, Code.Br_S      // System.Double::IsNaN(float64)
		};

		[Detect(Code.Clt)]
		public static Boolean Is_Clt(this VirtualOpCode ins)
		{
			return ins.DelegateMethod.Matches(
				Code.Call, Code.Brtrue_S, Code.Ldc_I4_0, Code.Br_S, Code.Ldc_I4_1,
				Code.Callvirt, Code.Ldloc_2, Code.Call, Code.Ret
			) && ins.DelegateMethod.MatchesIndirect(
				// Helper method used elsewhere?
				Code.Ldarg_0, Code.Castclass, Code.Callvirt, Code.Ldarg_1, Code.Castclass,
				Code.Callvirt, Code.Clt, Code.Stloc_0, Code.Ldloc_0, Code.Ret
			);
		}

		[Detect(Code.Clt_Un)]
		public static Boolean Is_Clt_Un(this VirtualOpCode ins)
		{
			return ins.DelegateMethod.Matches(new Code[] {
				Code.Call, Code.Brtrue_S, Code.Ldc_I4_0, Code.Br_S, Code.Ldc_I4_1,
				Code.Callvirt, Code.Ldloc_2, Code.Call, Code.Ret
			}) && ins.DelegateMethod.MatchesIndirect(Pattern_Clt_Un);
		}

		/// <summary>
		/// OpCode pattern seen in the Greater-Than helper method.
		/// Used in: Cgt_Un
		/// </summary>
		/// <remarks>Greater-than for Double, Int32, Int64 but not-equal for other?</remarks>
		private static readonly Code[] Pattern_Cgt_Un = new Code[] {
			Code.Ldarg_0, Code.Castclass, Code.Callvirt, Code.Ldarg_1,
			Code.Castclass, Code.Callvirt, Code.Cgt_Un, Code.Stloc_0
		};

		[Detect(Code.Cgt)]
		/// <remarks>Unsure</remarks>
		public static Boolean Is_Cgt(this VirtualOpCode ins)
		{
			return ins.DelegateMethod.Matches(
				Code.Call, Code.Brtrue_S, Code.Ldc_I4_0, Code.Br_S, Code.Ldc_I4_1,
				Code.Callvirt, Code.Call, Code.Ret
			) && ins.DelegateMethod.MatchesIndirect(
				Code.Ldloc_S, Code.Ldloc_S, Code.Cgt, Code.Stloc_0, Code.Br_S,
				Code.Ldc_I4_0, Code.Stloc_0, Code.Ldloc_0, Code.Ret
			);

            //maybe 

            /*return ins.DelegateMethod.Matches(new Code[] {
                Code.Dup, Code.Ldloc_1, Code.Ldloc_0, Code.Call, Code.Brtrue_S, Code.Ldc_I4_0, Code.Br_S, Code.Ldc_I4_1,
                Code.Callvirt, Code.Call, Code.Ret
            }) && ins.DelegateMethod.MatchesIndirect(Code.Ldarg_0, Code.Ldfld, Code.Callvirt, Code.Ret)
            && ins.DelegateMethod.Calls().ToList()[3].ResolveMethodDef().Matches(Code.Cgt)
            && ins.DelegateMethod.Calls().ToList()[3].ResolveMethodDef().Matches(Code.Cgt, Code.Stloc_0, Code.Ldloc_0, Code.Ret);*/
        }

        [Detect(Code.Cgt_Un)]
		public static Boolean Is_Cgt_Un(this VirtualOpCode ins)
		{
			return ins.DelegateMethod.Matches(new Code[] {
                Code.Dup, Code.Ldloc_1, Code.Ldloc_0, Code.Call, Code.Brtrue_S, Code.Ldc_I4_0, Code.Br_S, Code.Ldc_I4_1,
				Code.Callvirt, Code.Call, Code.Ret
			}) && ins.DelegateMethod.MatchesIndirect(Code.Ldarg_0, Code.Ldfld, Code.Callvirt, Code.Ret)
            && ins.DelegateMethod.Calls().ToList()[3].ResolveMethodDef().Matches(Code.Cgt)
            && ins.DelegateMethod.Calls().ToList()[3].ResolveMethodDef().Matches(Code.Ceq, Code.Stloc_0, Code.Ldloc_0, Code.Ret);
		}

		[Detect(Code.Ckfinite)]
		public static Boolean Is_Ckfinite(this VirtualOpCode ins)
		{
			var sub = ins.Find(new Code[] {
				Code.Ldloc_0, Code.Callvirt, Code.Call, Code.Brtrue_S,
				Code.Ldloc_0, Code.Callvirt, Code.Call, Code.Brfalse_S,
				Code.Ldstr, Code.Newobj, Code.Throw
			});

			return sub != null
				&& ((IMethod)sub[2].Operand).FullName.Contains("System.Double::IsNaN")
				&& ((IMethod)sub[6].Operand).FullName.Contains("System.Double::IsInfinity");
		}

		[Detect(Code.Dup)]
		public static Boolean Is_Dup(this VirtualOpCode ins)
		{
			return ins.DelegateMethod.MatchesEntire(
				Code.Ldarg_0, Code.Call, Code.Stloc_0, Code.Ldloc_0, Code.Callvirt,
				Code.Stloc_1, Code.Ldarg_0, Code.Ldloc_0, Code.Call, Code.Ldarg_0,
				Code.Ldloc_1, Code.Call, Code.Ret
			);
		}

		[Detect(Code.Endfinally)]
		public static Boolean Is_Endfinally(this VirtualOpCode ins)
		{
			return ins.DelegateMethod.MatchesEntire(
				Code.Ldarg_0, Code.Call, Code.Ret
			) && ins.DelegateMethod.MatchesIndirect(
				Code.Ldarg_0, Code.Ldfld, Code.Callvirt, Code.Ldarg_0, Code.Ldloca_S,
				Code.Call, Code.Call, Code.Ret
			);
		}

		[Detect(Code.Isinst)]
		public static Boolean Is_Isinst(this VirtualOpCode ins)
		{
			return ins.DelegateMethod.Matches(
				Code.Call, Code.Brfalse_S, Code.Ldarg_0, Code.Ldloc_2, Code.Call, Code.Ret,
				Code.Ldarg_0, Code.Newobj, Code.Call, Code.Ret
			);
		}

		[Detect(Code.Jmp)]
		public static Boolean Is_Jmp(this VirtualOpCode ins)
		{
			IMethod called;
			var sub = ins.DelegateMethod.Find(
				Code.Callvirt, Code.Call, Code.Stloc_1, Code.Ldarg_0, Code.Ldfld, Code.Stloc_2
			);
			return sub != null
				&& (called = sub[1].Operand as IMethod) != null
				&& called.FullName.Contains("System.Reflection.MethodBase");
		}

		/// <summary>
		/// OpCode pattern seen in the Throw, Rethrow helper methods.
		/// </summary>
		public static readonly Code[] Pattern_Throw = new Code[] {
			Code.Ldarg_1, Code.Isinst, Code.Stloc_0, Code.Ldloc_0, Code.Brfalse_S, Code.Ldloc_0,
			Code.Call, Code.Ldarg_1, Code.Call, Code.Ret
		};

		public static Boolean _Is_Throw(VirtualOpCode ins, MethodDef helper)
		{
			var matches = Helpers.FindOpCodePatterns(helper.Body.Instructions, Pattern_Throw);
			return matches.Count == 1 && matches[0].Length == Pattern_Throw.Length;
		}

		[Detect(Code.Throw)]
		public static Boolean Is_Throw(this VirtualOpCode ins)
		{
			return ins.MatchesEntire(new Code[] {
				Code.Ldarg_0, Code.Call, Code.Stloc_0, Code.Ldarg_0, Code.Ldloc_0,
				Code.Callvirt, Code.Call, Code.Ret
			}) && _Is_Throw(ins, ((MethodDef)ins.DelegateMethod.Body.Instructions[6].Operand));
		}

		[Detect(Code.Rethrow)]
		public static Boolean Is_Rethrow(this VirtualOpCode ins)
		{
			var sub = ins.Find(new Code[] {
				Code.Newobj, Code.Throw, Code.Ldarg_0, Code.Ldarg_0, Code.Ldfld,
				Code.Callvirt, Code.Callvirt, Code.Stfld, Code.Ldarg_0, Code.Ldfld,
				Code.Call, Code.Ret
			});
			return sub != null && _Is_Throw(ins, ((MethodDef)sub[10].Operand));
		}

		[Detect(Code.Ldfld)]
		public static Boolean Is_Ldfld(this VirtualOpCode ins)
		{
			return ins.Matches(new Code[] {
				Code.Ldarg_0, Code.Ldloc_1, Code.Ldloc_2, Code.Callvirt, Code.Ldloc_1,
				Code.Callvirt, Code.Call, Code.Call, Code.Ret
			}) && ins.DelegateMethod.Calls().Any((called) =>
			{
				return called.FullName.Contains("System.Reflection.FieldInfo::GetValue");
			});
		}

		[Detect(Code.Ldflda)]
		public static Boolean Is_Ldflda(this VirtualOpCode ins)
		{
			MethodDef method;
			var sub = ins.DelegateMethod.Find(new Code[] {
				Code.Call, Code.Stloc_S, Code.Ldarg_0, Code.Call, Code.Stloc_1, Code.Ldloc_1, Code.Isinst
			});
			return sub != null
				&& ins.DelegateMethod.Calls().Any((called) =>
                {
                    return called.ResolveMethodDef().ReturnType.FullName.Contains("System.Reflection.FieldInfo");
                });
		}

		[Detect(Code.Ldftn)]
		public static Boolean Is_Ldftn(this VirtualOpCode ins)
		{
			MethodDef called = null;
			var sub = ins.DelegateMethod.Find(new Code[] {
				Code.Ldarg_0, Code.Newobj, Code.Dup, Code.Ldloc_1,
				Code.Callvirt, Code.Call, Code.Ret
			});
			return sub != null
				&& (called = ((MethodDef)sub[4].Operand)) != null
				&& called.Parameters.Count >= 2
				&& called.Parameters[1].Type.FullName.Equals("System.Reflection.MethodBase");
		}

		[Detect(Code.Ldlen)]
		public static Boolean Is_Ldlen(this VirtualOpCode ins)
		{
			return ins.MatchesEntire(new Code[] {
				Code.Ldarg_0, Code.Call, Code.Callvirt, Code.Castclass, Code.Stloc_0, Code.Ldarg_0,
				Code.Newobj, Code.Dup, Code.Ldloc_0, Code.Callvirt, Code.Callvirt,
				Code.Call, Code.Ret
			}) && ((IMethod)ins.DelegateMethod.Body.Instructions[9].Operand)
				  .FullName.Contains("System.Array::get_Length");
		}

		[Detect(Code.Ldsfld)]
		public static Boolean Is_Ldsfld(this VirtualOpCode ins)
		{
			return ins.DelegateMethod.Matches(new Code[] {
				Code.Ldarg_0, Code.Ldloc_1, Code.Ldnull, Code.Callvirt, Code.Ldloc_1,
				Code.Callvirt, Code.Call, Code.Call, Code.Ret
			}) && ins.DelegateMethod.Calls().Any((called) =>
			{
				return called.FullName.Contains("System.Reflection.FieldInfo::GetValue");
			});
		}

		[Detect(Code.Ldsflda)]
		public static Boolean Is_Ldsflda(this VirtualOpCode ins)
		{
			MethodDef method;
			var sub = ins.DelegateMethod.Find(new Code[] {
				Code.Ldarg_0, Code.Newobj, Code.Stloc_2, Code.Ldloc_2, Code.Ldloc_1,
				Code.Callvirt, Code.Ldloc_2, Code.Call, Code.Ret
			});
			return sub != null
				&& (method = (sub[5].Operand as MethodDef)) != null
				&& method.Parameters.Count == 2
				&& method.Parameters[1].Type.FullName.Contains("System.Reflection.FieldInfo");
		}

		[Detect(Code.Ldobj)]
		public static Boolean Is_Ldobj(this VirtualOpCode ins)
		{
			return ins.DelegateMethod.MatchesEntire(new Code[] {
				Code.Ldarg_1, Code.Castclass, Code.Callvirt, Code.Stloc_0, Code.Ldarg_0,
				Code.Ldloc_0, Code.Call, Code.Stloc_1, Code.Ldarg_0, Code.Ldloc_1,
				Code.Call, Code.Ret
			}) && ins.DelegateMethod.MatchesIndirect(new Code[] {
				Code.Ldarg_0, Code.Call, Code.Stloc_0, Code.Ldarg_0, Code.Ldarg_0,
				Code.Ldloc_0, Code.Call, Code.Callvirt, Code.Ldarg_1, Code.Call,
				Code.Call, Code.Ret
			}) && ((MethodDef)ins.DelegateMethod.Body.Instructions[2].Operand)
				  .ReturnType.FullName.Equals("System.Int32")
			&& ((MethodDef)ins.DelegateMethod.Body.Instructions[6].Operand)
				  .ReturnType.FullName.Equals("System.Type");
		}

		[Detect(Code.Ldstr)]
		public static Boolean Is_Ldstr(this VirtualOpCode ins)
		{
			return ins.MatchesEntire(new Code[] {
				Code.Ldarg_1, Code.Castclass, Code.Callvirt, Code.Stloc_0, Code.Ldarg_0,
				Code.Ldloc_0, Code.Call, Code.Stloc_1, Code.Ldarg_0, Code.Newobj,
				Code.Stloc_2, Code.Ldloc_2, Code.Ldloc_1, Code.Callvirt, Code.Ldloc_2,
				Code.Call, Code.Ret
			}) && ((MethodDef)ins.DelegateMethod.Body.Instructions[6].Operand)
				  .ReturnType.FullName.Equals("System.String");
		}

		[Detect(Code.Ldnull)]
		public static Boolean Is_Ldnull(this VirtualOpCode ins)
		{
			return ins.MatchesEntire(new Code[] {
				Code.Ldarg_0, Code.Newobj, Code.Call, Code.Ret
			});
		}

		[Detect(Code.Ldtoken)]
		public static Boolean Is_Ldtoken(this VirtualOpCode ins)
		{
			// Checks delegate method tail
			// Could also check: System.Reflection.FieldInfo::get_Type/Field/MethodHandle(),
			// there are 1 of each of these calls
			return ins.DelegateMethod.Matches(
				Code.Box, Code.Stloc_2, Code.Br_S, Code.Newobj, Code.Throw, Code.Ldarg_0, Code.Newobj, Code.Dup, Code.Ldloc_2,
				Code.Callvirt, Code.Call, Code.Ret
			);
		}


        [Detect(Code.Ldtoken)]
        public static Boolean Is_Ldtoken_Codeflow(this VirtualOpCode ins)
        {
            // Checks delegate method tail
            // Could also check: System.Reflection.FieldInfo::get_Type/Field/MethodHandle(),
            // there are 1 of each of these calls
            return ins.DelegateMethod.Matches(
                Code.Call, Code.Callvirt, Code.Box, Code.Stloc_2, Code.Ldarg_0, Code.Newobj, Code.Dup, Code.Ldloc_2,
                Code.Callvirt, Code.Call, Code.Ret
            );
        }

        [Detect(Code.Ldvirtftn)]
		public static Boolean Is_Ldvirtftn(this VirtualOpCode ins)
		{
			MethodDef called = null;
			var sub = ins.DelegateMethod.Find(new Code[] {
				Code.Ldarg_0, Code.Newobj, Code.Stloc_S, Code.Ldloc_S, Code.Ldloc_3,
				Code.Callvirt, Code.Ldloc_S, Code.Call, Code.Ret
			});
			return sub != null
				&& (called = ((MethodDef)sub[5].Operand)) != null
				&& called.Parameters.Count >= 2
				&& called.Parameters[1].Type.FullName.Equals("System.Reflection.MethodBase");
		}

		[Detect(Code.Leave)]
		public static Boolean Is_Leave(this VirtualOpCode ins)
		{
			return ins.DelegateMethod.MatchesEntire(
				Code.Ldarg_1, Code.Castclass, Code.Callvirt, Code.Stloc_0, Code.Ldarg_0,
				Code.Ldnull, Code.Ldloc_0, Code.Call, Code.Ret
			);
		}

		[Detect(Code.Newarr)]
		public static Boolean Is_Newarr(this VirtualOpCode ins)
		{
			var sub = ins.DelegateMethod.Find(
				Code.Throw, Code.Ldloc_1, Code.Call, Code.Stloc_S
			);
			return sub != null
				&& ((IMethod)sub[2].Operand).FullName.Contains("System.Array::CreateInstance");
		}

		[Detect(Code.Newobj)]
		public static Boolean Is_Newobj(this VirtualOpCode ins)
		{
			return ins.Matches(new Code[] {
				Code.Ldarg_0, Code.Ldloc_2, Code.Ldnull, Code.Ldloc_3, Code.Ldc_I4_0,
				Code.Call, Code.Stloc_S, Code.Leave_S
			});
		}

		[Detect(Code.Nop, ExpectsMultiple = true)]
		public static Boolean Is_Nop(this VirtualOpCode ins)
		{
			// Three virtual opcodes match this. One of them makes sense to be Nop,
			// unsure what the other two are (maybe Endfault, Endfilter).
			OperandType operandType;
			return ins.DelegateMethod.MatchesEntire(Code.Ret)
				&& ins.TryGetOperandType(out operandType)
				&& operandType == OperandType.InlineNone;
		}

		[Detect(Code.Pop)]
		public static Boolean Is_Pop(this VirtualOpCode ins)
		{
			MethodDef method = null;
			return ins.MatchesEntire(new Code[] {
				Code.Ldarg_0, Code.Call, Code.Pop, Code.Ret
			}) && (method = ins.DelegateMethod.Body.Instructions[1].Operand as MethodDef) != null
			   && method.MatchesEntire(new Code[] {
				   Code.Ldarg_0, Code.Ldfld, Code.Callvirt, Code.Ret
			   });
		}

		[Detect(Code.Ret)]
		public static Boolean Is_Ret(this VirtualOpCode ins)
		{
			return ins.MatchesEntire(new Code[] {
				Code.Ldarg_0, Code.Call, Code.Ret
			}) && ((MethodDef)ins.DelegateMethod.Body.Instructions[1].Operand).MatchesEntire(new Code[] {
				Code.Ldarg_0, Code.Ldc_I4_1, Code.Stfld, Code.Ret
			});
		}

		[Detect(Code.Stfld)]
		public static Boolean Is_Stfld(this VirtualOpCode ins)
		{
			return ins.Matches(new Code[] {
				Code.Ldarg_0, Code.Ldloc_1, Code.Ldloc_0, Code.Ldnull, Code.Call,
				Code.Call, Code.Ret
			}) && ins.DelegateMethod.Calls().Any((called) =>
			{
				return called.FullName.Contains("System.Reflection.FieldInfo::SetValue");
			});
		}

		[Detect(Code.Stsfld)]
		public static Boolean Is_Stsfld(this VirtualOpCode ins)
		{
			return ins.Matches(new Code[] {
				Code.Ldloc_1, Code.Ldnull, Code.Ldloc_2, Code.Callvirt, Code.Callvirt,
				Code.Ret
			}) && ins.DelegateMethod.Calls().Any((called) =>
			{
				return called.FullName.Contains("System.Reflection.FieldInfo::SetValue");
			});
		}

		[Detect(Code.Switch)]
		public static Boolean Is_Switch(this VirtualOpCode ins)
		{
			return ins.DelegateMethod.Matches(
				Code.Blt_S, Code.Ret, Code.Ldloc_3, Code.Ldloc_2, Code.Conv_U, Code.Ldelem,
				Code.Callvirt, Code.Stloc_S, Code.Ldarg_0, Code.Ldloc_S, Code.Call, Code.Ret
			);
		}

		[Detect(Code.Unbox)]
		public static Boolean Is_Unbox(this VirtualOpCode ins)
		{
			OperandType operandType;
			return ins.DelegateMethod.MatchesEntire(Code.Ret)
				&& ins.TryGetOperandType(out operandType)
				&& operandType == OperandType.InlineType;
		}

		[Detect(Code.Unbox_Any)]
		public static Boolean Is_Unbox_Any(this VirtualOpCode ins)
		{
			return ins.DelegateMethod.Matches(
				Code.Ldarg_0, Code.Call, Code.Callvirt, Code.Ldloc_1,
				Code.Call, Code.Stloc_2, Code.Ldarg_0, Code.Ldloc_2, Code.Call, Code.Ret
			);
        }

        [Detect(Code.Endfilter)]
        public static Boolean Is_Endfilter(this VirtualOpCode ins)
        {
            return ins.DelegateMethod.Matches(Code.Newobj, Code.Callvirt, Code.Ldarg_0, Code.Ldc_I4_0, Code.Stfld, Code.Ldarg_0, Code.Call, Code.Ret) && 
				ins.DelegateMethod.Calls().Count() == 7 &&
				ins.DelegateMethod.Calls().ToList()[6].ResolveMethodDef().Matches(Code.Ldfld, Code.Callvirt, Code.Ldarg_0, Code.Ldloca_S, Code.Call, Code.Call, Code.Ret);
        }
    }
}
