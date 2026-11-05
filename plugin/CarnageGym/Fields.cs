using System;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CarnageGym
{
	/// <summary>
	/// Reads and writes named fields on game objects without a compile-time game reference.
	/// </summary>
	static class Fields
	{
		/// <summary>
		/// Return the field value, or null when the field does not exist.
		/// </summary>
		public static object Read(object instance, string fieldName)
		{
			if (instance == null)
				return null;
			var field = AccessTools.Field(instance.GetType(), fieldName);
			if (field == null)
				return null;
			return field.GetValue(instance);
		}

		/// <summary>
		/// Write a bool or number into a field, converting to the field's type.
		/// </summary>
		public static void Write(object instance, string fieldName, object value)
		{
			var field = AccessTools.Field(instance.GetType(), fieldName);
			if (field == null)
				return;
			field.SetValue(instance, ConvertTo(field.FieldType, value));
		}

		/// <summary>
		/// True when the named type and field both exist.
		/// </summary>
		public static bool Exists(string typeName, string fieldName)
		{
			var type = AccessTools.TypeByName(typeName);
			return type != null && AccessTools.Field(type, fieldName) != null;
		}

		/// <summary>
		/// Convert boxed game values to a bool. Numbers are true when non-zero.
		/// </summary>
		public static bool AsBool(object raw)
		{
			if (raw is bool value)
				return value;
			if (raw == null)
				return false;
			try
			{
				return Convert.ToSingle(raw, CultureInfo.InvariantCulture) != 0f;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// Convert a boxed number. Null and failures become 0.
		/// </summary>
		public static float AsFloat(object raw)
		{
			if (raw == null)
				return 0f;
			try
			{
				return Convert.ToSingle(raw, CultureInfo.InvariantCulture);
			}
			catch (Exception)
			{
				return 0f;
			}
		}

		/// <summary>
		/// Right is 1 and left is -1. A missing value defaults to facing right.
		/// </summary>
		public static float Facing(object raw)
		{
			if (raw is bool value)
				return value ? 1f : -1f;
			var number = AsFloat(raw);
			if (number < 0f)
				return -1f;
			if (number > 0f)
				return 1f;
			return 1f;
		}

		static object ConvertTo(Type type, object value)
		{
			if (type == typeof(bool))
				return AsBool(value);
			if (type == typeof(int))
				return Convert.ToInt32(value, CultureInfo.InvariantCulture);
			if (type == typeof(float))
				return Convert.ToSingle(value, CultureInfo.InvariantCulture);
			if (type == typeof(double))
				return Convert.ToDouble(value, CultureInfo.InvariantCulture);
			return value;
		}
	}
}
