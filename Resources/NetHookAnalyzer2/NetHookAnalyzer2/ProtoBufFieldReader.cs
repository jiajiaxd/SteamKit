using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NetHookAnalyzer2
{
	class ProtoBufFieldReader
	{
		const int MaxNestedDepth = 8;
		static readonly UTF8Encoding Utf8 = new( encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true );

		public static Dictionary<int, List<object>> ReadProtobuf(Stream stream)
		{
			ArgumentNullException.ThrowIfNull( stream );
			return ReadProtobuf( stream, depth: 0 );
		}

		static Dictionary<int, List<object>> ReadProtobuf(Stream stream, int depth)
		{
			if ( depth > MaxNestedDepth )
			{
				return null;
			}

			var fields = new Dictionary<int, List<object>>();

			while ( stream.Position < stream.Length )
			{
				if ( !TryReadVarint( stream, out var tag ) )
				{
					return null;
				}

				if ( tag == 0 )
				{
					break;
				}

				var field = (int)( tag >> 3 );
				var wireType = (int)( tag & 0b111 );
				if ( field <= 0 || !TryReadFieldValue( stream, wireType, depth + 1, out var fieldValue ) )
				{
					return null;
				}

				if ( fields.TryGetValue( field, out var values ) )
				{
					values.Add( fieldValue );
				}
				else
				{
					values = [ fieldValue ];
					fields[ field ] = values;
				}
			}

			return fields.Count > 0 ? fields : null;
		}

		static bool TryReadFieldValue(Stream stream, int wireType, int depth, out object value)
		{
			switch ( wireType )
			{
				case 0:
					if ( TryReadVarint( stream, out var varintValue ) )
					{
						value = ToDisplayInteger( varintValue );
						return true;
					}

					break;

				case 1:
					if ( TryReadFixed64( stream, out var fixed64Value ) )
					{
						value = fixed64Value;
						return true;
					}

					break;

				case 2:
					if ( TryReadLengthDelimited( stream, out var lengthDelimitedValue ) )
					{
						value = ReadLengthDelimitedValue( lengthDelimitedValue, depth );
						return true;
					}

					break;

				case 5:
					if ( TryReadFixed32( stream, out var fixed32Value ) )
					{
						value = fixed32Value;
						return true;
					}

					break;
			}

			value = null;
			return false;
		}

		static object ReadLengthDelimitedValue(byte[] data, int depth)
		{
			using var ms = new MemoryStream( data, writable: false );
			var nestedMessage = ReadProtobuf( ms, depth );
			if ( nestedMessage != null && ms.Position == ms.Length )
			{
				return nestedMessage;
			}

			if ( TryReadDisplayableUtf8( data, out var text ) )
			{
				return text;
			}

			return data;
		}

		static object ToDisplayInteger(ulong value)
		{
			if ( value <= long.MaxValue )
			{
				return (long)value;
			}

			return value;
		}

		static bool TryReadDisplayableUtf8(byte[] data, out string value)
		{
			try
			{
				value = Utf8.GetString( data );
			}
			catch (DecoderFallbackException)
			{
				value = null;
				return false;
			}

			foreach ( var ch in value )
			{
				if ( char.IsControl( ch ) && ch != '\r' && ch != '\n' && ch != '\t' )
				{
					value = null;
					return false;
				}

				if ( ch > 0x7E )
				{
					value = null;
					return false;
				}
			}

			return true;
		}

		static bool TryReadLengthDelimited(Stream stream, out byte[] data)
		{
			if ( !TryReadVarint( stream, out var length ) || length > int.MaxValue )
			{
				data = null;
				return false;
			}

			data = new byte[ length ];
			return TryReadExactly( stream, data );
		}

		static bool TryReadFixed32(Stream stream, out uint value)
		{
			var buffer = new byte[ sizeof( uint ) ];
			if ( !TryReadExactly( stream, buffer ) )
			{
				value = default;
				return false;
			}

			value = BitConverter.ToUInt32( buffer, 0 );
			return true;
		}

		static bool TryReadFixed64(Stream stream, out ulong value)
		{
			var buffer = new byte[ sizeof( ulong ) ];
			if ( !TryReadExactly( stream, buffer ) )
			{
				value = default;
				return false;
			}

			value = BitConverter.ToUInt64( buffer, 0 );
			return true;
		}

		static bool TryReadVarint(Stream stream, out ulong value)
		{
			value = 0;

			for ( var shift = 0; shift < 64; shift += 7 )
			{
				var read = stream.ReadByte();
				if ( read < 0 )
				{
					return false;
				}

				value |= (ulong)( read & 0x7F ) << shift;
				if ( ( read & 0x80 ) == 0 )
				{
					return true;
				}
			}

			return false;
		}

		static bool TryReadExactly(Stream stream, byte[] buffer)
		{
			var totalRead = 0;
			while ( totalRead < buffer.Length )
			{
				var bytesRead = stream.Read( buffer, totalRead, buffer.Length - totalRead );
				if ( bytesRead == 0 )
				{
					return false;
				}

				totalRead += bytesRead;
			}

			return true;
		}
	}
}
