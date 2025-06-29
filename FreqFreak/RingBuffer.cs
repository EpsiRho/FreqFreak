using System.Collections;

// CircularBuffer thanks to https://github.com/joaoportela/CircularBuffer-CSharp/blob/master/CircularBuffer/CircularBuffer.cs
// Modified to enable thread saftey 
namespace FreqFreak
{
    /// <inheritdoc/>
    /// <summary>
    /// Thread Safe Circular buffer.
    /// 
    /// When writing to a full buffer:
    /// PushBack -> removes this[0] / Front()
    /// PushFront -> removes this[Size-1] / Back()
    /// 
    /// this implementation is inspired by
    /// http://www.boost.org/doc/libs/1_53_0/libs/circular_buffer/doc/circular_buffer.html
    /// because I liked their interface.
    /// </summary>
    public class CircularBuffer<T> : IEnumerable<T>
    {
        private readonly T[] _buffer;
        private readonly object _lock = new object();

        /// <summary>
        /// The _start. Index of the first element in buffer.
        /// </summary>
        private int _start;

        /// <summary>
        /// The _end. Index after the last element in the buffer.
        /// </summary>
        private int _end;

        /// <summary>
        /// The _size. Buffer size.
        /// </summary>
        private int _size;

        public CircularBuffer(int capacity)
            : this(capacity, new T[] { })
        {
        }

        public CircularBuffer(int capacity, T[] items)
        {
            if (capacity < 1)
            {
                throw new ArgumentException(
                    "Circular buffer cannot have negative or zero capacity.", nameof(capacity));
            }
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }
            if (items.Length > capacity)
            {
                throw new ArgumentException(
                    "Too many items to fit circular buffer", nameof(items));
            }

            _buffer = new T[capacity];

            Array.Copy(items, _buffer, items.Length);
            _size = items.Length;

            _start = 0;
            _end = _size == capacity ? 0 : _size;
        }

        /// <summary>
        /// Maximum capacity of the buffer. Elements pushed into the buffer after
        /// maximum capacity is reached (IsFull = true), will remove an element.
        /// </summary>
        public int Capacity { get { return _buffer.Length; } }

        /// <summary>
        /// Boolean indicating if Circular is at full capacity.
        /// Adding more elements when the buffer is full will
        /// cause elements to be removed from the other end
        /// of the buffer.
        /// </summary>
        public bool IsFull
        {
            get
            {
                lock (_lock)
                {
                    return _size == Capacity;
                }
            }
        }

        /// <summary>
        /// True if has no elements.
        /// </summary>
        public bool IsEmpty
        {
            get
            {
                lock (_lock)
                {
                    return _size == 0;
                }
            }
        }

        /// <summary>
        /// Current buffer size (the number of elements that the buffer has).
        /// </summary>
        public int Size
        {
            get
            {
                lock (_lock)
                {
                    return _size;
                }
            }
        }

        /// <summary>
        /// Alternative to Size for compatibility with standard collections
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _size;
                }
            }
        }

        /// <summary>
        /// Element at the front of the buffer - this[0].
        /// </summary>
        public T Front()
        {
            lock (_lock)
            {
                ThrowIfEmpty();
                return _buffer[_start];
            }
        }

        /// <summary>
        /// Element at the back of the buffer - this[Size - 1].
        /// </summary>
        public T Back()
        {
            lock (_lock)
            {
                ThrowIfEmpty();
                return _buffer[(_end != 0 ? _end : Capacity) - 1];
            }
        }

        /// <summary>
        /// Index access to elements in buffer.
        /// </summary>
        public T this[int index]
        {
            get
            {
                lock (_lock)
                {
                    if (_size == 0)
                    {
                        throw new IndexOutOfRangeException($"Cannot access index {index}. Buffer is empty");
                    }
                    if (index >= _size)
                    {
                        throw new IndexOutOfRangeException($"Cannot access index {index}. Buffer size is {_size}");
                    }
                    int actualIndex = InternalIndex(index);
                    return _buffer[actualIndex];
                }
            }
            set
            {
                lock (_lock)
                {
                    if (_size == 0)
                    {
                        throw new IndexOutOfRangeException($"Cannot access index {index}. Buffer is empty");
                    }
                    if (index >= _size)
                    {
                        throw new IndexOutOfRangeException($"Cannot access index {index}. Buffer size is {_size}");
                    }
                    int actualIndex = InternalIndex(index);
                    _buffer[actualIndex] = value;
                }
            }
        }

        /// <summary>
        /// Pushes a new element to the back of the buffer.
        /// </summary>
        public void PushBack(T item)
        {
            lock (_lock)
            {
                if (_size == Capacity) // IsFull
                {
                    _buffer[_end] = item;
                    Increment(ref _end);
                    _start = _end;
                }
                else
                {
                    _buffer[_end] = item;
                    Increment(ref _end);
                    ++_size;
                }
            }
        }

        /// <summary>
        /// Pushes a new element to the front of the buffer.
        /// </summary>
        public void PushFront(T item)
        {
            lock (_lock)
            {
                if (_size == Capacity) // IsFull
                {
                    Decrement(ref _start);
                    _end = _start;
                    _buffer[_start] = item;
                }
                else
                {
                    Decrement(ref _start);
                    _buffer[_start] = item;
                    ++_size;
                }
            }
        }

        /// <summary>
        /// Removes the element at the back of the buffer.
        /// </summary>
        public void PopBack()
        {
            lock (_lock)
            {
                ThrowIfEmpty("Cannot take elements from an empty buffer.");
                Decrement(ref _end);
                _buffer[_end] = default(T);
                --_size;
            }
        }

        /// <summary>
        /// Removes the element at the front of the buffer.
        /// </summary>
        public void PopFront()
        {
            lock (_lock)
            {
                ThrowIfEmpty("Cannot take elements from an empty buffer.");
                _buffer[_start] = default(T);
                Increment(ref _start);
                --_size;
            }
        }

        /// <summary>
        /// Clears the contents of the array.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _start = 0;
                _end = 0;
                _size = 0;
                Array.Clear(_buffer, 0, _buffer.Length);
            }
        }

        /// <summary>
        /// Copies the buffer contents to an array, according to the logical
        /// contents of the buffer (i.e. independent of the internal 
        /// order/contents)
        /// </summary>
        /// <returns>A new array with a copy of the buffer contents.</returns>
        public T[] ToArray()
        {
            lock (_lock)
            {
                if (_size == 0)
                {
                    return new T[0];
                }

                T[] newArray = new T[_size];
                int newArrayOffset = 0;

                // Get segments under lock to ensure consistency
                var segments = ToArraySegmentsUnsafe();

                foreach (ArraySegment<T> segment in segments)
                {
                    Array.Copy(segment.Array, segment.Offset, newArray, newArrayOffset, segment.Count);
                    newArrayOffset += segment.Count;
                }
                return newArray;
            }
        }

        /// <summary>
        /// Get the contents of the buffer as 2 ArraySegments.
        /// Respects the logical contents of the buffer, where
        /// each segment and items in each segment are ordered
        /// according to insertion.
        ///
        /// Fast: does not copy the array elements.
        /// Useful for methods like <c>Send(IList&lt;ArraySegment&lt;Byte&gt;&gt;)</c>.
        /// 
        /// <remarks>Segments may be empty.</remarks>
        /// </summary>
        /// <returns>An IList with 2 segments corresponding to the buffer content.</returns>
        public IList<ArraySegment<T>> ToArraySegments()
        {
            lock (_lock)
            {
                return ToArraySegmentsUnsafe();
            }
        }

        /// <summary>
        /// Internal non thread safe call - must be called under lock.
        /// </summary>
        private IList<ArraySegment<T>> ToArraySegmentsUnsafe()
        {
            return new[] { ArrayOneUnsafe(), ArrayTwoUnsafe() };
        }

        #region IEnumerable<T> implementation
        /// <summary>
        /// Returns an enumerator that iterates through this buffer.
        /// Creates a snapshot to avoid holding the lock during enumeration.
        /// </summary>
        public IEnumerator<T> GetEnumerator()
        {
            // Create a snapshot to avoid holding lock during enumeration
            T[] snapshot = ToArray();
            return ((IEnumerable<T>)snapshot).GetEnumerator();
        }
        #endregion

        #region IEnumerable implementation
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        #endregion

        private void ThrowIfEmpty(string message = "Cannot access an empty buffer.")
        {
            if (_size == 0)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        /// Increments the provided index variable by one, wrapping around if necessary.
        /// </summary>
        private void Increment(ref int index)
        {
            if (++index == Capacity)
            {
                index = 0;
            }
        }

        /// <summary>
        /// Decrements the provided index variable by one, wrapping around if necessary.
        /// </summary>
        private void Decrement(ref int index)
        {
            if (index == 0)
            {
                index = Capacity;
            }
            index--;
        }

        /// <summary>
        /// Converts the index in the argument to an index in _buffer
        /// </summary>
        private int InternalIndex(int index)
        {
            return _start + (index < (Capacity - _start) ? index : index - Capacity);
        }

        #region Array items easy access - unsafe versions (must be called under lock)

        private ArraySegment<T> ArrayOneUnsafe()
        {
            if (_size == 0)
            {
                return new ArraySegment<T>(new T[0]);
            }
            else if (_start < _end)
            {
                return new ArraySegment<T>(_buffer, _start, _end - _start);
            }
            else
            {
                return new ArraySegment<T>(_buffer, _start, _buffer.Length - _start);
            }
        }

        private ArraySegment<T> ArrayTwoUnsafe()
        {
            if (_size == 0 || _start < _end)
            {
                return new ArraySegment<T>(new T[0]);
            }
            else
            {
                return new ArraySegment<T>(_buffer, 0, _end);
            }
        }
        #endregion
    }
}