// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Runtime.CompilerServices;

namespace Balansoft.AIAddin.Helpers
{
    /// <summary>
    /// Soporte de Diseño por Contrato (Design by Contract - DbC).
    /// Define y valida precondiciones (Requires), postcondiciones (Ensures) e invariantes (Invariant).
    /// </summary>
    public static class BlsContract
    {
        #region Precondiciones (Requires)

        /// <summary>
        /// Exige que una condición de precondición sea verdadera.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Requires(bool condition, string? message = null)
        {
            if (!condition)
            {
                throw new BlsPreconditionException(message ?? "Violación de precondición del contrato.");
            }
        }

        /// <summary>
        /// Exige que un argumento de referencia no sea nulo.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T RequiresNotNull<T>(T? argument, string paramName, string? message = null) where T : class
        {
            if (argument is null)
            {
                throw new ArgumentNullException(paramName, message ?? $"El argumento '{paramName}' no puede ser nulo.");
            }
            return argument;
        }

        /// <summary>
        /// Exige que un argumento de cadena no sea nulo, vacío ni consista únicamente en espacios en blanco.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string RequiresNotNullOrWhiteSpace(string? argument, string paramName, string? message = null)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                throw new ArgumentException(message ?? $"El argumento '{paramName}' no puede ser nulo ni estar vacío.", paramName);
            }
            return argument!;
        }

        /// <summary>
        /// Exige que un valor se encuentre dentro del rango válido esperado.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RequiresRange(bool condition, string paramName, string? message = null)
        {
            if (!condition)
            {
                throw new ArgumentOutOfRangeException(paramName, message ?? $"El argumento '{paramName}' se encuentra fuera del rango permitido.");
            }
        }

        /// <summary>
        /// Exige que el estado del objeto sea válido para invocar la operación.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RequiresState(bool condition, string? message = null)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message ?? "El estado del objeto no cumple la precondición requerida para esta operación.");
            }
        }

        #endregion

        #region Postcondiciones (Ensures)

        /// <summary>
        /// Garantiza que una condición posterior sea satisfecha al completar un método.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Ensures(bool condition, string? message = null)
        {
            if (!condition)
            {
                throw new BlsPostconditionException(message ?? "Violación de postcondición del contrato.");
            }
        }

        /// <summary>
        /// Garantiza que el resultado devuelto por un método no sea nulo.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T EnsuresNotNull<T>(T? result, string? message = null) where T : class
        {
            if (result is null)
            {
                throw new BlsPostconditionException(message ?? "El resultado del método no puede ser nulo según el contrato.");
            }
            return result;
        }

        #endregion

        #region Invariantes (Invariant)

        /// <summary>
        /// Garantiza que una condición invariante de estado se mantenga válida.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Invariant(bool condition, string? message = null)
        {
            if (!condition)
            {
                throw new BlsInvariantException(message ?? "Violación de invariante de estado de clase.");
            }
        }

        #endregion
    }

    /// <summary>
    /// Excepción lanzada ante una violación de precondición en DbC.
    /// </summary>
    [Serializable]
    public class BlsPreconditionException : Exception
    {
        public BlsPreconditionException(string message) : base(message) { }
        public BlsPreconditionException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Excepción lanzada ante una violación de postcondición en DbC.
    /// </summary>
    [Serializable]
    public class BlsPostconditionException : Exception
    {
        public BlsPostconditionException(string message) : base(message) { }
        public BlsPostconditionException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Excepción lanzada ante una violación de invariante de clase en DbC.
    /// </summary>
    [Serializable]
    public class BlsInvariantException : Exception
    {
        public BlsInvariantException(string message) : base(message) { }
        public BlsInvariantException(string message, Exception inner) : base(message, inner) { }
    }
}
