// ****************************************************************************
///*!	\file LargePartitionedList.cs
// *	\brief A List of Lists to avoid the Large Object Heap (LOH)
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2014-11-19
// *	\author Abed Haque, KF5HFJ
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using System.Diagnostics;

namespace Flex.Util
{
    public class LargePartitionedList<T> : IList<T>
    {
        private const int INNER_LIST_MAX_SIZE_POWERS_OF_TWO = 14;
        private const int INNER_LIST_MAX_SIZE = 1 << INNER_LIST_MAX_SIZE_POWERS_OF_TWO; // 2^14 = 16384
        private int _total_count = 0;
        //private int _last_set_index = 0;
        //private int _last_get_index = 0;
        //private IList<T> _last_set_inner_list;
        //private IList<T> _last_get_inner_list;
        private IList<IList<T>> _lists;

        public LargePartitionedList()
        {
            //_maxCountPerList = maxCountPerList;
            _lists = new List<IList<T>> { new List<T>(INNER_LIST_MAX_SIZE) };
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _lists.SelectMany(list => list).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(T item)
        {
            var lastList = _lists[_lists.Count - 1];
            if (lastList.Count == INNER_LIST_MAX_SIZE)
            {
                lastList = new List<T>(INNER_LIST_MAX_SIZE);
                _lists.Add(lastList);
            }
            lastList.Add(item);
            _total_count++;
        }

        /// <summary>
        /// Removes all elements of the LargePartionedList
        /// </summary>
        public void Clear()
        {
            while (_lists.Count > 1) _lists.RemoveAt(1);
            _lists[0].Clear();

            _total_count = 0;
        }

        /// <summary>
        /// Sets all elements in the LargePartionedList to 
        /// a value.
        /// </summary>
        /// <param name="value">The value to set all elements to</param>
        public void SetAllTo(T value)
        {
            for (int i = 0; i < _lists.Count; i++)
            {
                for (int k = 0; k < _lists[i].Count; k++)
                    _lists[i][k] = value;
            }
        }

        public bool Contains(T item)
        {
            return _lists.Any(sublist => sublist.Contains(item));
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(T[] array, int startIndex, int cnt)
        {
            for (int i = 0; i < cnt; i++)
            {
                array[i] = this[i + startIndex];
            }
        }

        public bool Remove(T item)
        {
            // Evil, Linq with sideeffects
            _total_count--;
            return _lists.Any(sublist => sublist.Remove(item));
        }

        public void RemoveLastItem()
        {
            var lastList = _lists[_lists.Count - 1];
            lastList.RemoveAt(0);

            if (lastList.Count == 0)
                _lists.RemoveAt(_lists.Count - 1);

            _total_count--;
        }

        public int Count
        {
            //get { return _lists.Sum(subList => subList.Count); }
            get { return _total_count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public int IndexOf(T item)
        {
            int index = _lists.Select((subList, i) => subList.IndexOf(item) * i).Max();
            return (index > -1) ? index : -1;
        }

        public void Insert(int index, T item)
        {
            throw new NotImplementedException();
        }

        public void RemoveAt(int index)
        {
            throw new NotImplementedException();
        }

        public T this[int index]
        {
            get
            {
                if (index >= _total_count)
                {
                    throw new IndexOutOfRangeException();
                }

                return _getItemByIndex(index);          
            }
            set
            {
                if (index >= _total_count)
                    throw new IndexOutOfRangeException();

                var inner_list = _getInnerListByIndex(index);
                inner_list[index % INNER_LIST_MAX_SIZE] = value;
            }
        }

        private T _getItemByIndex(int index)
        {
            var inner_list = _getInnerListByIndex(index);
            return inner_list[index % INNER_LIST_MAX_SIZE];
        }

        private IList<T> _getInnerListByIndex(int index)
        {
            return _lists[index >> INNER_LIST_MAX_SIZE_POWERS_OF_TWO];
        }
    }
}
